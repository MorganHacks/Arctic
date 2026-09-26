using System.Text.Json;
using MorganHacks.Applications.Services;
using Npgsql;

namespace MorganHacks.Applications.Data;

public sealed class PostgresAnnouncementResponseStore(NpgsqlDataSource dataSource)
{
    public async Task<Dictionary<Guid, AnnouncementTally>> TalliesAsync(
        Guid[] ids, Guid personId, CancellationToken ct)
    {
        var tallies = new Dictionary<Guid, AnnouncementTally>();
        if (ids.Length == 0) return tallies;
        await using var cmd = dataSource.CreateCommand("""
            SELECT announcement_id, choice, count(*)::int, bool_or(person_id = @personId)
              FROM applications.announcement_responses
             WHERE announcement_id = ANY(@ids)
             GROUP BY announcement_id, choice
            """);
        cmd.Parameters.AddWithValue("ids", ids);
        cmd.Parameters.AddWithValue("personId", personId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            var choice = reader.GetInt16(1);
            var tally = tallies.GetValueOrDefault(id) ?? new AnnouncementTally(new int[4], null);
            tally.Counts[choice] = reader.GetInt32(2);
            tallies[id] = reader.GetBoolean(3) ? tally with { Choice = choice } : tally;
        }
        return tallies;
    }

    public async Task<AnnouncementVoteResult> VoteAsync(
        Guid id, Guid personId, int choice, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        AnnouncementContent? content;
        await using (var read = new NpgsqlCommand("""
            SELECT content, retracted_at FROM applications.announcements
             WHERE id = @id AND publish_at <= now() AND event_id = (
                 SELECT event_id FROM applications.applications
                  WHERE person_id = @personId ORDER BY started_at DESC LIMIT 1
             ) FOR UPDATE
            """, connection, transaction))
        {
            read.Parameters.AddWithValue("id", id);
            read.Parameters.AddWithValue("personId", personId);
            await using var reader = await read.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return new(null, "No such announcement.", 404);
            if (!reader.IsDBNull(1)) return new(null, "This announcement has been taken down.", 410);
            content = reader.IsDBNull(0) ? null :
                JsonSerializer.Deserialize<AnnouncementContent>(reader.GetString(0), AnnouncementContent.Json);
        }
        if (content?.IsQuestion != true || content.Options is null)
            return new(null, "This announcement does not accept votes.", 400);
        if (choice < 0 || choice >= content.Options.Length)
            return new(null, "Choose one of the available answers.", 400);

        var conflict = content.Kind == "quiz" ? "DO NOTHING" :
            "DO UPDATE SET choice = EXCLUDED.choice, answered_at = now()";
        await using var write = new NpgsqlCommand($"""
            INSERT INTO applications.announcement_responses (announcement_id, person_id, choice)
            VALUES (@id, @personId, @choice)
            ON CONFLICT (announcement_id, person_id) {conflict}
            """, connection, transaction);
        write.Parameters.AddWithValue("id", id);
        write.Parameters.AddWithValue("personId", personId);
        write.Parameters.AddWithValue("choice", (short)choice);
        await write.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return new(content, null, 200);
    }
}
