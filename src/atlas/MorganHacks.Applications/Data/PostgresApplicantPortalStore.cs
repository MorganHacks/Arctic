using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using Npgsql;

namespace MorganHacks.Applications.Data;

/// <summary>
/// The applicant's own view of <c>applications.*</c>.
/// </summary>
/// <remarks>
/// Every statement in this file narrows on <c>person_id</c>, and none of them
/// takes an application id from anywhere. That is the whole safety story for
/// the portal: there is no query here that could be pointed at somebody else's
/// row, so no endpoint above it can be talked into pointing one.
/// </remarks>
public sealed class PostgresApplicantPortalStore(NpgsqlDataSource dataSource)
    : IApplicantPortalStore
{
    /// <summary>
    /// Which application the portal means by "yours".
    /// </summary>
    /// <remarks>
    /// Newest first, because somebody who applied in 2026 and again in 2027 is
    /// asking about 2027. Written once and reused by the update so a save can
    /// never land on a different row from the one that was shown.
    /// </remarks>
    private const string Mine = """
        SELECT id FROM applications.applications
         WHERE person_id = @personId
         ORDER BY started_at DESC
         LIMIT 1
        """;

    public async Task<ApplicantApplication?> FindForPersonAsync(
        Guid personId, CancellationToken ct = default)
    {
        // decisions_announced_at is compared against now() rather than only
        // tested for presence, so the team can set the moment in advance and
        // have the portal change by itself rather than by somebody running an
        // UPDATE at the exact minute of the announcement.
        const string sql = $"""
            SELECT a.id,
                   a.event_id,
                   a.status,
                   e.decisions_announced_at IS NOT NULL
                       AND e.decisions_announced_at <= now() AS announced,
                   a.submitted_at,
                   a.rsvp_deadline,
                   e.starts_at,
                   a.first_name, a.last_name, a.school, a.shirt_size,
                   a.dietary_needs, a.accessibility_needs
              FROM applications.applications a
              JOIN applications.events e ON e.id = a.event_id
             WHERE a.id = ({Mine})
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("personId", personId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new ApplicantApplication(
            reader.GetGuid(0),
            reader.GetGuid(1),
            ApplicationStatuses.Parse(reader.GetString(2)),
            reader.GetBoolean(3),
            await reader.IsDBNullAsync(4, ct) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            await reader.IsDBNullAsync(5, ct) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            await reader.IsDBNullAsync(6, ct) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            new ApplicantProfile
            {
                FirstName = await Text(reader, 7, ct),
                LastName = await Text(reader, 8, ct),
                School = await Text(reader, 9, ct),
                ShirtSize = await Text(reader, 10, ct),
                DietaryNeeds = await Text(reader, 11, ct),
                AccessibilityNeeds = await Text(reader, 12, ct),
            });
    }

    public async Task<ProfileSave> SaveProfileAsync(
        Guid personId, ApplicantProfile profile, CancellationToken ct = default)
    {
        // Six columns and no others. Status, email, event, the answers and
        // every agreement timestamp are absent from this statement, so a field
        // an applicant should not be able to move cannot be moved by adding a
        // key to the request body.
        //
        // The status test lives in the WHERE clause rather than in the caller
        // because a reviewer can decide this application between the read that
        // drew the form and the write that submits it.
        const string sql = $"""
            UPDATE applications.applications
               SET first_name          = @firstName,
                   last_name           = @lastName,
                   school              = @school,
                   shirt_size          = @shirtSize,
                   dietary_needs       = @dietaryNeeds,
                   accessibility_needs = @accessibilityNeeds
             WHERE id = ({Mine})
               AND status = ANY(@open)
            RETURNING id
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("open", ProfileEditing.OpenWire);
        cmd.Parameters.AddWithValue("firstName", Value(profile.FirstName));
        cmd.Parameters.AddWithValue("lastName", Value(profile.LastName));
        cmd.Parameters.AddWithValue("school", Value(profile.School));
        cmd.Parameters.AddWithValue("shirtSize", Value(profile.ShirtSize));
        cmd.Parameters.AddWithValue("dietaryNeeds", Value(profile.DietaryNeeds));
        cmd.Parameters.AddWithValue("accessibilityNeeds", Value(profile.AccessibilityNeeds));

        if (await cmd.ExecuteScalarAsync(ct) is Guid)
        {
            return ProfileSave.Saved;
        }

        // Nothing was written, and the two reasons need different sentences on
        // the screen: one is "you have not started yet", the other is "this is
        // no longer yours to change".
        await using var exists = dataSource.CreateCommand(
            $"SELECT EXISTS ({Mine})");
        exists.Parameters.AddWithValue("personId", personId);

        return await exists.ExecuteScalarAsync(ct) is true
            ? ProfileSave.Closed
            : ProfileSave.NoApplication;
    }

    /// <inheritdoc />
    public async Task<string?> CheckInCodeAsync(Guid personId, CancellationToken ct = default)
    {
        // Read first, mint second. The read is the common path by an enormous
        // margin -- a code is created once and shown every time the screen is
        // opened after that -- and going straight to an UPDATE would move
        // updated_at on every view of a page that changes nothing.
        const string read = $"""
            SELECT status, check_in_code
              FROM applications.applications
             WHERE id = ({Mine})
            """;

        await using var current = dataSource.CreateCommand(read);
        current.Parameters.AddWithValue("personId", personId);

        string status;
        await using (var reader = await current.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            status = reader.GetString(0);
            if (!await reader.IsDBNullAsync(1, ct))
            {
                return reader.GetString(1);
            }
        }

        if (!CheckInCode.IssuedWire.Contains(status))
        {
            return null;
        }

        // check_in_code IS NULL in the WHERE clause, so two portal loads
        // racing each other cannot mint two codes: the second writes nothing
        // and reads back what the first stored. The status is tested again
        // here for the same reason the profile write tests it -- the read
        // above is not in this statement's transaction.
        //
        // A collision against applications_check_in_code_key would surface
        // here as a failed request rather than a retry. Sixty bits against a
        // few thousand codes makes that roughly a one in a quadrillion event,
        // and the recovery is a page refresh, which mints a different code.
        const string mint = $"""
            UPDATE applications.applications
               SET check_in_code = @code,
                   check_in_code_issued_at = now()
             WHERE id = ({Mine})
               AND check_in_code IS NULL
               AND status = ANY(@issued)
            RETURNING check_in_code
            """;

        await using var write = dataSource.CreateCommand(mint);
        write.Parameters.AddWithValue("personId", personId);
        write.Parameters.AddWithValue("code", CheckInCode.Issue());
        write.Parameters.AddWithValue("issued", CheckInCode.IssuedWire);

        if (await write.ExecuteScalarAsync(ct) is string minted)
        {
            return minted;
        }

        // Nothing was written. Either the race above, or a decision landed
        // between the two statements. Re-reading answers both honestly:
        // whatever is on the row now is the code this person should be shown,
        // and null is the right answer if there is not one.
        await using var again = dataSource.CreateCommand(
            $"SELECT check_in_code FROM applications.applications WHERE id = ({Mine})");
        again.Parameters.AddWithValue("personId", personId);
        return await again.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>
    /// Reads a nullable text column, collapsing an empty string to null.
    /// </summary>
    /// <remarks>
    /// So the portal has one thing to test for "not answered". A column
    /// holding <c>''</c> and one holding NULL mean the same thing to an
    /// applicant and would otherwise render differently.
    /// </remarks>
    private static async Task<string?> Text(NpgsqlDataReader reader, int column, CancellationToken ct)
    {
        if (await reader.IsDBNullAsync(column, ct))
        {
            return null;
        }

        var value = reader.GetString(column);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Blank in, NULL stored.
    /// </summary>
    /// <remarks>
    /// Clearing a field has to be possible — somebody who listed a dietary
    /// need last year and no longer has one must be able to say so — and an
    /// empty string in the column would show on the catering export as an
    /// answer rather than as no answer.
    /// </remarks>
    private static object Value(string? text) =>
        string.IsNullOrWhiteSpace(text) ? DBNull.Value : text.Trim();
}
