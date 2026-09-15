using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Applications.Forms;

/// <summary>
/// The write behind an ungated survey's Submit button.
/// </summary>
/// <remarks>
/// Writes the same table <see cref="PostgresRespondentStore"/> does, with the
/// person left out. That is the whole difference, and keeping it to one table
/// is what lets <see cref="PostgresSurveyResponseStore"/> read a survey with
/// the query it already had — an organizer reading a survey wants all of it,
/// and a screen that shows half is worse than one that shows none because
/// nothing on it says a half is missing.
/// <para>
/// <c>application_id</c> is never set here and the schema refuses it. An
/// answer nobody signed in to give cannot be about a particular application,
/// and a row that claimed otherwise would read as attribution on a screen
/// whose whole job is to say who answered.
/// </para>
/// </remarks>
public sealed class PostgresAnonymousSubmissionStore(NpgsqlDataSource dataSource)
    : IAnonymousSubmissionStore
{
    private static readonly JsonSerializerOptions Json = new();

    public async Task<Guid> RecordAsync(
        Guid formId,
        int formVersion,
        Guid? submissionKey,
        IReadOnlyDictionary<string, JsonElement> answers,
        CancellationToken ct = default)
    {
        // One statement for both cases, which is not a trick.
        // form_submissions_form_attempt_key is a partial index over the rows
        // that have a key, so a row without one is not in the index and cannot
        // conflict with anything — the ON CONFLICT below is simply never
        // reached for it. That is the behaviour we want spelled as the schema
        // rather than as a branch: a keyless submission is always its own
        // response.
        //
        // The WHERE clause is what names that partial index as the arbiter.
        // Without it Postgres refuses to plan the statement rather than
        // quietly picking the wrong index, which is the right way for this to
        // fail if somebody changes the index and not this.
        const string sql = """
            INSERT INTO applications.form_submissions
                (form_id, form_version, submission_key, answers)
            VALUES (@formId, @version, @key, @answers)
            ON CONFLICT (form_id, submission_key) WHERE submission_key IS NOT NULL
            DO UPDATE SET answers = excluded.answers,
                          form_version = excluded.form_version,
                          updated_at = now()
            RETURNING id
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("formId", formId);
        cmd.Parameters.AddWithValue("version", formVersion);
        cmd.Parameters.AddWithValue("key", (object?)submissionKey ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("answers", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(answers, Json),
        });

        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
