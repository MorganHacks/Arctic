using System.Runtime.CompilerServices;
using System.Text.Json;
using MorganHacks.Applications.Services;
using Npgsql;

namespace MorganHacks.Applications.Forms;

/// <summary>
/// Reads submitted applications back out of <c>applications.applications</c>.
/// </summary>
/// <remarks>
/// The mirror of <see cref="PostgresSubmissionStore"/>. That one splits an
/// answer between a promoted column and the <c>responses</c> jsonb; this one
/// puts the two halves back together under the question's key, so a caller
/// sees one answer set and never the shape of the table underneath.
/// <para>
/// Scoped to an event rather than to a form, because there is no form id on an
/// application — the schema ties a submission to the event and the version it
/// answered, and the unique index allows exactly one application form per
/// event. That equivalence is what makes this correct and it is only true for
/// application forms; see <c>FormResponseEndpoints</c>, which is where the
/// check that a form is one of those lives.
/// </para>
/// </remarks>
public sealed class PostgresResponseStore(NpgsqlDataSource dataSource) : IResponseStore
{
    /// <summary>
    /// The columns every read needs, whatever the form asks.
    /// </summary>
    /// <remarks>
    /// None of these can collide with a promoted answer's column:
    /// <see cref="AnswerColumns"/> deliberately excludes the submission's own
    /// fields, so nothing a form can name lands here.
    /// </remarks>
    private const string Fixed =
        "id, submitted_at, form_version, responses, "
        + "resume_key, resume_filename, resume_size";

    private const int FixedCount = 7;

    /// <summary>
    /// Only what has actually been submitted.
    /// </summary>
    /// <remarks>
    /// A row is created the moment somebody starts the form, so the table also
    /// holds every abandoned half-filled attempt. Those are not responses —
    /// nobody gave them to us — and <c>submitted_at</c> is both the honest test
    /// and the column the ordering index is built on.
    /// </remarks>
    private const string Submitted =
        "event_id = @eventId AND submitted_at IS NOT NULL";

    public async Task<ResponsePage> PageAsync(
        Guid eventId,
        FormQuestions questions,
        ResponseCursor? after,
        int limit,
        CancellationToken ct = default)
    {
        // One more than asked for, so "is there another page" is answered by
        // what came back rather than by a second count(*) over the same
        // predicate. The extra row is discarded below.
        var sql = $"SELECT {Select(questions)} FROM applications.applications WHERE {Submitted}"
                  + (after is null ? string.Empty : " AND (submitted_at, id) < (@at, @after)")
                  + " ORDER BY submitted_at DESC, id DESC LIMIT @limit";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("limit", limit + 1);

        if (after is { } cursor)
        {
            // A row-value comparison rather than the usual
            // "submitted_at < @at OR (submitted_at = @at AND id < @after)".
            // They mean the same thing and only this one can use the index as
            // a single range scan.
            cmd.Parameters.AddWithValue("at", cursor.SubmittedAt);
            cmd.Parameters.AddWithValue("after", cursor.Id);
        }

        var rows = new List<FormResponse>(limit + 1);
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                rows.Add(Read(reader, questions));
            }
        }

        // The row past the page is the whole point of asking for it: it says
        // there is more without saying anything about what, and it never
        // reaches a caller.
        var more = rows.Count > limit;
        var items = more ? rows[..limit] : rows;

        return new ResponsePage(
            items,
            more ? new ResponseCursor(items[^1].SubmittedAt, items[^1].Id) : null);
    }

    public async Task<FormResponse?> ByIdAsync(
        Guid eventId,
        Guid responseId,
        FormQuestions questions,
        CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            $"SELECT {Select(questions)} FROM applications.applications "
            + $"WHERE {Submitted} AND id = @id");

        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("id", responseId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader, questions) : null;
    }

    public async IAsyncEnumerable<FormResponse> AllAsync(
        Guid eventId,
        FormQuestions questions,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            $"SELECT {Select(questions)} FROM applications.applications "
            + $"WHERE {Submitted} ORDER BY submitted_at DESC, id DESC");

        cmd.Parameters.AddWithValue("eventId", eventId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            yield return Read(reader, questions);
        }
    }

    /// <summary>
    /// The select list: the fixed columns, then whatever this form promotes.
    /// </summary>
    /// <remarks>
    /// The extra names are interpolated because a column cannot be a bound
    /// parameter. Safe only because <see cref="FormQuestions.Columns"/> is
    /// built from <see cref="AnswerColumns.TryKindOf"/> — the same allow-list
    /// the write path checks against — and never from anything that arrived
    /// with the request.
    /// </remarks>
    private static string Select(FormQuestions questions) =>
        questions.Columns.Count == 0
            ? Fixed
            : $"{Fixed}, {string.Join(", ", questions.Columns)}";

    private static FormResponse Read(NpgsqlDataReader reader, FormQuestions questions)
    {
        var version = reader.GetInt32(2);
        var keys = questions.KeysByColumn(version);

        var answers = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        // The promoted half. Read against the version's own questions, so a
        // column this form no longer uses still comes back under the key the
        // applicant answered it as.
        for (var i = FixedCount; i < reader.FieldCount; i++)
        {
            if (!keys.TryGetValue(reader.GetName(i), out var key) || reader.IsDBNull(i))
            {
                // Either that version never asked this, or it was left blank.
                // Both are absences, and an absent key is how a caller tells
                // "not answered" from "answered with nothing".
                continue;
            }

            answers[key] = Promoted(reader, i);
        }

        // The rest, already keyed by question when it was written. Last, so a
        // form that somehow wrote an answer to both places reads back as the
        // jsonb says — which is the copy that carries its own key.
        using (var document = JsonDocument.Parse(reader.GetString(3)))
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                // Cloned, because the element is a window onto a document this
                // block is about to dispose.
                answers[property.Name] = property.Value.Clone();
            }
        }

        return new FormResponse(
            reader.GetGuid(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            version,
            answers,
            reader.IsDBNull(4)
                ? null
                : new StoredResume(
                    reader.GetString(4),

                    // Rows written before there was anywhere to put the bytes
                    // have a name and no key, so they never reach here. A key
                    // with no name is not a shape that has existed, and a
                    // fallback beats a null on a screen somebody is reading.
                    reader.IsDBNull(5) ? "resume.pdf" : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6)));
    }

    /// <summary>
    /// One promoted answer, read as whatever its column actually holds.
    /// </summary>
    /// <remarks>
    /// The kind comes from <see cref="AnswerColumns"/> rather than from the
    /// question, for the same reason the write path looks it up:
    /// <c>mlh_coc_agreed_at</c> is a timestamptz and <c>mlh_marketing_opt_in</c>
    /// is a boolean, and both are a tick in the same checkbox on the page.
    /// <para>
    /// An agreement therefore reads back as the moment it was ticked rather
    /// than as <c>true</c>. That is the record the column exists to keep —
    /// "they agreed at 14:03 on the 12th" is the answer to the question anyone
    /// ever asks about a consent — and it is strictly more than a flag.
    /// </para>
    /// </remarks>
    private static JsonElement Promoted(NpgsqlDataReader reader, int ordinal)
    {
        AnswerColumns.TryKindOf(reader.GetName(ordinal), out var kind);

        return kind switch
        {
            ColumnKind.Integer => JsonSerializer.SerializeToElement(reader.GetInt32(ordinal)),
            ColumnKind.Boolean => JsonSerializer.SerializeToElement(reader.GetBoolean(ordinal)),
            ColumnKind.Timestamp => JsonSerializer.SerializeToElement(
                reader.GetFieldValue<DateTimeOffset>(ordinal)),
            _ => JsonSerializer.SerializeToElement(reader.GetString(ordinal)),
        };
    }
}
