using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Applications.Forms;

/// <summary>
/// The applicant's own side of a form that is not the application form.
/// </summary>
/// <remarks>
/// Reads <c>applications.applications</c> to answer "who is this and what have
/// they already told us", and writes <c>applications.form_submissions</c> to
/// record what they answer. It never writes the application itself: an RSVP is
/// a reply about an application, not an edit of one, and letting a survey
/// rewrite the row a reviewer already read is a different feature with a
/// different audit story.
/// </remarks>
public sealed class PostgresRespondentStore(NpgsqlDataSource dataSource) : IRespondentStore
{
    private static readonly JsonSerializerOptions Json = new();

    /// <summary>
    /// The promoted answers, as a select list.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="AnswerColumns.All"/> rather than written out, so
    /// a column added to the allow-list is prefilled without anybody
    /// remembering to add it here. Interpolating names into SQL is safe for
    /// exactly one reason — every one of them came from that allow-list, which
    /// is the same list the write path checks against — and for no other.
    /// </remarks>
    private static readonly string PromotedColumns =
        string.Join(", ", AnswerColumns.All.Select(entry => $"a.{entry.Key}"));

    public async Task<OnFile?> FindOnFileAsync(
        Guid eventId, string email, CancellationToken ct = default)
    {
        // Matched on lower(email) against the dedupe index, which is the same
        // comparison that decided this address could only apply once.
        const string sql = """
            SELECT id, email, person_id,
                   nullif(trim(concat_ws(' ', first_name, last_name)), '')
              FROM applications.applications
             WHERE event_id = @eventId AND lower(email) = lower(@email)
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("email", email.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new OnFile(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(2) ? null : reader.GetGuid(2));
    }

    public async Task LinkPersonAsync(
        Guid applicationId, Guid personId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("""
            UPDATE applications.applications
               SET person_id = @personId
             WHERE id = @id AND person_id IS NULL
            """);

        cmd.Parameters.AddWithValue("id", applicationId);
        cmd.Parameters.AddWithValue("personId", personId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Respondent?> ForPersonAsync(
        Guid eventId, Guid personId, Guid formId, CancellationToken ct = default)
    {
        // The application and this person's last answer to this form in one
        // round trip. The left join is what makes a reopened RSVP show what
        // they said last time; without a previous answer it costs nothing.
        var sql = $"""
            SELECT a.id, a.email, a.first_name, a.last_name, a.status,
                   a.mlh_coc_agreed_at IS NOT NULL, a.mlh_data_sharing_at IS NOT NULL,
                   a.responses, s.answers, {PromotedColumns}
              FROM applications.applications a
              LEFT JOIN applications.form_submissions s
                     ON s.form_id = @formId AND s.person_id = a.person_id
             WHERE a.event_id = @eventId AND a.person_id = @personId
             ORDER BY a.created_at DESC
             LIMIT 1
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("formId", formId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var known = Known(reader);

        return new Respondent(
            personId,
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetBoolean(5),
            reader.GetBoolean(6),
            known);
    }

    /// <summary>How many columns come before the promoted ones.</summary>
    private const int FixedCount = 9;

    /// <summary>
    /// Everything this person has already told us, keyed the way a question
    /// would be.
    /// </summary>
    /// <remarks>
    /// Three sources, in increasing order of authority.
    /// <list type="number">
    /// <item>
    /// The application's <c>responses</c> jsonb, which is keyed by
    /// <see cref="FormField.Key"/> already and needs no translating.
    /// </item>
    /// <item>
    /// The promoted columns, keyed by column name. That is the convention
    /// <see cref="StartingQuestions"/> uses — every one of its questions has a
    /// key equal to the column it writes to — and it is what makes a school or a
    /// shirt size prefill on a form whose author never saw the application.
    /// A question routed at a column under some other key does not prefill
    /// from it, which is the honest answer: two different keys are two
    /// different questions as far as anything else in this system is
    /// concerned.
    /// </item>
    /// <item>
    /// Their last answer to this very form, which wins over both. Somebody
    /// reopening an RSVP is looking at what they said, not at what their
    /// application said a month earlier.
    /// </item>
    /// </list>
    /// </remarks>
    private static IReadOnlyDictionary<string, JsonElement> Known(NpgsqlDataReader reader)
    {
        var known = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        Merge(known, reader.IsDBNull(7) ? null : reader.GetString(7));

        for (var i = 0; i < AnswerColumns.All.Count; i++)
        {
            var ordinal = FixedCount + i;
            if (reader.IsDBNull(ordinal))
            {
                continue;
            }

            var (column, kind) = (AnswerColumns.All[i].Key, AnswerColumns.All[i].Value);
            if (Held(reader, ordinal, kind) is { } value)
            {
                known[column] = value;
            }
        }

        Merge(known, reader.IsDBNull(8) ? null : reader.GetString(8));

        return known;
    }

    /// <summary>One stored column, shaped like the answer that produced it.</summary>
    /// <remarks>
    /// The mirror of <c>PostgresSubmissionStore.ColumnValue</c>. A timestamp
    /// column is the odd one: it holds the moment an agreement was given, and
    /// what a form does with it is tick a box — so it reads back as
    /// <c>true</c> and never as a date, which is not a question anybody was
    /// asked.
    /// </remarks>
    private static JsonElement? Held(NpgsqlDataReader reader, int ordinal, ColumnKind kind) =>
        kind switch
        {
            ColumnKind.Text => JsonSerializer.SerializeToElement(reader.GetString(ordinal)),
            ColumnKind.Integer => JsonSerializer.SerializeToElement(reader.GetInt32(ordinal)),
            ColumnKind.Boolean => JsonSerializer.SerializeToElement(reader.GetBoolean(ordinal)),
            ColumnKind.Timestamp => JsonSerializer.SerializeToElement(true),
            _ => null,
        };

    /// <summary>Folds a jsonb object of answers into what we already have.</summary>
    /// <remarks>
    /// Cloned out of the document, because a <see cref="JsonElement"/> stops
    /// being readable the moment the <see cref="JsonDocument"/> behind it is
    /// disposed — and these outlive this method by a whole request.
    /// </remarks>
    private static void Merge(Dictionary<string, JsonElement> known, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            known[property.Name] = property.Value.Clone();
        }
    }

    public async Task<Guid> RecordAsync(
        Guid formId,
        int formVersion,
        Respondent respondent,
        IReadOnlyDictionary<string, JsonElement> answers,
        CancellationToken ct = default)
    {
        // Upserted against form_submissions_form_person_key. Somebody changing
        // their mind about an RSVP is not a second reply, and a double-tapped
        // Submit on a slow phone is not one either.
        const string sql = """
            INSERT INTO applications.form_submissions
                (form_id, form_version, person_id, application_id, answers)
            VALUES (@formId, @version, @personId, @applicationId, @answers)
            ON CONFLICT (form_id, person_id) DO UPDATE
                SET answers = excluded.answers,
                    form_version = excluded.form_version,
                    application_id = excluded.application_id,
                    updated_at = now()
            RETURNING id
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("formId", formId);
        cmd.Parameters.AddWithValue("version", formVersion);
        cmd.Parameters.AddWithValue("personId", respondent.PersonId);
        cmd.Parameters.AddWithValue(
            "applicationId", (object?)respondent.ApplicationId ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("answers", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(answers, Json),
        });

        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
