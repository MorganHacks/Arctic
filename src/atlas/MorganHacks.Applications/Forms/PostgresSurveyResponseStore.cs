using System.Runtime.CompilerServices;
using System.Text.Json;
using Npgsql;

namespace MorganHacks.Applications.Forms;

/// <summary>
/// Reading back what somebody answered on a form that is not an application.
/// </summary>
/// <remarks>
/// Surveys, RSVPs, and anything else a signed-in person fills in once. Their
/// answers go to <c>applications.form_submissions</c>, which had a writer and
/// no reader: an organizer could publish a survey, watch somebody answer it,
/// and find nothing on the responses screen — because that screen reads
/// <c>applications.applications</c>, which is a different table holding a
/// different thing.
/// <para>
/// A separate store rather than a branch inside
/// <see cref="PostgresResponseStore"/>, because the two are scoped
/// differently and that difference is not incidental. An application belongs
/// to an event and is read per event; a survey submission belongs to a form
/// and is read per form. One store taking both an event and a form and using
/// whichever suited is how a query eventually gets the wrong one.
/// </para>
/// <para>
/// Nothing is promoted to a column here, so unlike the application reader
/// there is no per-version column mapping and no <see cref="FormQuestions"/>
/// to consult: the jsonb was written keyed by question and reads back the same
/// way. The version is carried so a screen can still show which questions were
/// on the page at the time.
/// </para>
/// </remarks>
public sealed class PostgresSurveyResponseStore(NpgsqlDataSource dataSource)
    : ISurveyResponseStore
{
    /// <summary>
    /// What every read here selects.
    /// </summary>
    /// <remarks>
    /// Note what is <em>not</em> in the WHERE clause of any query below: a
    /// condition on <c>person_id</c>. Since 0027 that column is nullable and an
    /// ungated survey writes rows with nothing in it, so every one of these
    /// reads returns anonymous and signed-in answers together without a branch,
    /// a union or a second cursor. That was the reason for putting both in one
    /// table: an organizer reading a survey wants all of it, and a list that
    /// silently holds back half is worse than one that shows none, because
    /// nothing on it says a half is missing.
    /// <para>
    /// The last column is the flag rather than the id. Which person answered is
    /// not something this screen shows or needs, and selecting an id nobody
    /// reads is how it ends up on a payload later by accident. Whether there
    /// was a person at all is the fact the screen is missing.
    /// </para>
    /// </remarks>
    private const string Columns =
        "id, submitted_at, form_version, answers, person_id IS NULL";

    /// <summary>
    /// Newest first, and by the same two-part key the application reader uses.
    /// </summary>
    /// <remarks>
    /// An OFFSET would shift under a reader while answers are still arriving.
    /// Two submissions can also share a <c>submitted_at</c> to the
    /// microsecond — a room told to fill in the feedback form at once is
    /// exactly that traffic — so the id breaks the tie, or a page either
    /// repeats a row forever or drops one.
    /// </remarks>
    public async Task<ResponsePage> PageAsync(
        Guid formId, ResponseCursor? after, int limit, CancellationToken ct = default)
    {
        // One more than asked for, so "is there another page" is answered
        // without a round trip that comes back empty.
        var sql = $"SELECT {Columns} FROM applications.form_submissions "
                  + "WHERE form_id = @formId"
                  + (after is null ? string.Empty : " AND (submitted_at, id) < (@at, @after)")
                  + " ORDER BY submitted_at DESC, id DESC LIMIT @limit";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("formId", formId);
        cmd.Parameters.AddWithValue("limit", limit + 1);

        if (after is { } cursor)
        {
            cmd.Parameters.AddWithValue("at", cursor.SubmittedAt);
            cmd.Parameters.AddWithValue("after", cursor.Id);
        }

        var items = new List<FormResponse>(limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            items.Add(Read(reader));
        }

        if (items.Count <= limit)
        {
            return new ResponsePage(items, null);
        }

        var last = items[limit - 1];
        items.RemoveAt(limit);

        return new ResponsePage(items, new ResponseCursor(last.SubmittedAt, last.Id));
    }

    /// <summary>
    /// One response on this form, or null.
    /// </summary>
    /// <remarks>
    /// The form is part of the lookup rather than checked afterwards, for the
    /// reason the application reader scopes by event: an id from one form read
    /// through another form's screen would otherwise answer, and the form in
    /// the URL is the only thing the caller was authorized against.
    /// </remarks>
    public async Task<FormResponse?> ByIdAsync(
        Guid formId, Guid responseId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            $"SELECT {Columns} FROM applications.form_submissions "
            + "WHERE form_id = @formId AND id = @id");

        cmd.Parameters.AddWithValue("formId", formId);
        cmd.Parameters.AddWithValue("id", responseId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>Every response, in the same order, streamed for the export.</summary>
    public async IAsyncEnumerable<FormResponse> AllAsync(
        Guid formId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            $"SELECT {Columns} FROM applications.form_submissions "
            + "WHERE form_id = @formId ORDER BY submitted_at DESC, id DESC");

        cmd.Parameters.AddWithValue("formId", formId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            yield return Read(reader);
        }
    }

    private static FormResponse Read(NpgsqlDataReader reader)
    {
        var answers = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        using (var document = JsonDocument.Parse(reader.GetString(3)))
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                // Cloned, because the element is a window onto a document this
                // block is about to dispose.
                answers[property.Name] = property.Value.Clone();
            }
        }

        // No resume. A survey has nowhere to attach one, and handing back a
        // null rather than leaving the shape out keeps one FormResponse for
        // both readers -- which is what lets the screen and the export stay
        // unchanged.
        return new FormResponse(
            reader.GetGuid(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.GetInt32(2),
            answers,
            Resume: null,
            Anonymous: reader.GetBoolean(4));
    }
}
