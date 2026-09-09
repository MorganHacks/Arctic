using MorganHacks.Applications.Services;
using Npgsql;

namespace MorganHacks.Applications.Data;

/// <summary>
/// The <c>applications.announcements</c> table, from the organizers' side.
/// </summary>
/// <remarks>
/// Every statement here is scoped by an id the caller named, which is the
/// opposite default from <see cref="PostgresApplicantPortalStore"/> and the
/// reason the applicant's read of this same table lives over there instead of
/// here.
/// <para>
/// There is no UPDATE of <c>body</c> anywhere in this file and there should
/// never be one. See <c>0024</c>: a notice people have already read is
/// retracted and replaced, not rewritten.
/// </para>
/// </remarks>
public sealed class PostgresAnnouncementStore(NpgsqlDataSource dataSource) : IAnnouncementStore
{
    /// <summary>
    /// Every column, in the one order <see cref="Read"/> reads them by.
    /// </summary>
    /// <remarks>
    /// Named once because the reads are positional, and a list that drifts
    /// between two queries is a retraction that arrives as a post date on one
    /// screen and not the other.
    /// </remarks>
    private const string Columns =
        "id, event_id, body, posted_at, posted_by, retracted_at, retracted_by";

    /// <summary>
    /// Newest first, with a tiebreak that never changes.
    /// </summary>
    /// <remarks>
    /// <c>posted_at</c> is transaction start time, so two announcements posted
    /// by two requests are already distinct to the microsecond — unlike
    /// <c>audit.entries</c>, where several rows genuinely share an instant
    /// because one action wrote them all. The id is here anyway so that the
    /// order is total rather than merely usually total: a feed that reshuffles
    /// two rows between refreshes is a feed somebody stops trusting.
    /// </remarks>
    private const string NewestFirst = "ORDER BY posted_at DESC, id DESC";

    public async Task<Announcement> PostAsync(
        Guid eventId, string body, Guid postedBy, CancellationToken ct = default)
    {
        // posted_at is the column default rather than a value from here, so
        // the timestamp on the row is the database's clock. The API's would be
        // a second opinion about when something was said, and during the one
        // weekend this is used the order things were said in is the whole
        // meaning of the screen.
        await using var cmd = dataSource.CreateCommand($"""
            INSERT INTO applications.announcements (event_id, body, posted_by)
            VALUES (@eventId, @body, @postedBy)
            RETURNING {Columns}
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("body", body);
        cmd.Parameters.AddWithValue("postedBy", postedBy);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Read(reader);
    }

    public async Task<IReadOnlyList<Announcement>> ForEventAsync(
        Guid eventId, CancellationToken ct = default)
    {
        // No filter on retracted_at. This is the list an organizer works from,
        // and one that hid what they took down would be indistinguishable from
        // one where the retraction never happened.
        await using var cmd = dataSource.CreateCommand($"""
            SELECT {Columns}
              FROM applications.announcements
             WHERE event_id = @eventId
             {NewestFirst}
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);

        var announcements = new List<Announcement>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            announcements.Add(Read(reader));
        }

        return announcements;
    }

    public async Task<Announcement?> RetractAsync(
        Guid id, Guid retractedBy, CancellationToken ct = default)
    {
        // retracted_at IS NULL in the WHERE clause, so a second retraction
        // writes nothing rather than overwriting the first retractor's name.
        // Who took it down first is the fact worth keeping; who agreed a
        // moment later is not.
        await using var cmd = dataSource.CreateCommand($"""
            UPDATE applications.announcements
               SET retracted_at = now(),
                   retracted_by = @retractedBy
             WHERE id = @id
               AND retracted_at IS NULL
            RETURNING {Columns}
            """);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("retractedBy", retractedBy);

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                return Read(reader);
            }
        }

        // Nothing was written, and the two reasons need different answers from
        // the endpoint: no such notice is a 404, and one that is already down
        // is the request having already got what it asked for.
        await using var again = dataSource.CreateCommand($"""
            SELECT {Columns}
              FROM applications.announcements
             WHERE id = @id
            """);
        again.Parameters.AddWithValue("id", id);

        await using var settled = await again.ExecuteReaderAsync(ct);
        return await settled.ReadAsync(ct) ? Read(settled) : null;
    }

    private static Announcement Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetFieldValue<DateTimeOffset>(3),
        reader.GetGuid(4),
        reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
        reader.IsDBNull(6) ? null : reader.GetGuid(6));
}
