using MorganHacks.Applications.Services;
using Npgsql;

namespace MorganHacks.Applications.Data;

public sealed class PostgresAnnouncementReactionStore(NpgsqlDataSource dataSource)
{
    public async Task<Dictionary<Guid, AnnouncementReactions>> TalliesAsync(
        Guid[] ids, Guid personId, CancellationToken ct)
    {
        var tallies = new Dictionary<Guid, AnnouncementReactions>();
        if (ids.Length == 0) return tallies;
        await using var cmd = dataSource.CreateCommand("""
            SELECT announcement_id, reaction, count(*)::int, bool_or(person_id = @personId)
              FROM applications.announcement_reactions
             WHERE announcement_id = ANY(@ids)
             GROUP BY announcement_id, reaction
            """);
        cmd.Parameters.AddWithValue("ids", ids);
        cmd.Parameters.AddWithValue("personId", personId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            var reaction = reader.GetString(1);
            var tally = tallies.GetValueOrDefault(id) ?? AnnouncementReactions.Empty();
            tally.Counts[reaction] = reader.GetInt32(2);
            tallies[id] = reader.GetBoolean(3) ? tally with { Choice = reaction } : tally;
        }
        return tallies;
    }

    public async Task<(string? Error, int StatusCode)> SetAsync(
        Guid id, Guid personId, string? reaction, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var read = new NpgsqlCommand("""
            SELECT retracted_at FROM applications.announcements
             WHERE id = @id AND publish_at <= now() AND event_id = (
                 SELECT event_id FROM applications.applications
                  WHERE person_id = @personId ORDER BY started_at DESC LIMIT 1
             ) FOR UPDATE
            """, connection, transaction))
        {
            read.Parameters.AddWithValue("id", id);
            read.Parameters.AddWithValue("personId", personId);
            await using var reader = await read.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return ("No such announcement.", 404);
            if (!reader.IsDBNull(0)) return ("This announcement has been taken down.", 410);
        }
        if (reaction is not null && !AnnouncementReactions.IsValid(reaction))
            return ("Choose one of the available reactions.", 400);

        await using var write = new NpgsqlCommand(reaction is null ? """
            DELETE FROM applications.announcement_reactions
             WHERE announcement_id = @id AND person_id = @personId
            """ : """
            INSERT INTO applications.announcement_reactions (announcement_id, person_id, reaction)
            VALUES (@id, @personId, @reaction)
            ON CONFLICT (announcement_id, person_id)
            DO UPDATE SET reaction = EXCLUDED.reaction, reacted_at = now()
            """, connection, transaction);
        write.Parameters.AddWithValue("id", id);
        write.Parameters.AddWithValue("personId", personId);
        if (reaction is not null) write.Parameters.AddWithValue("reaction", reaction);
        await write.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return (null, 200);
    }
}
