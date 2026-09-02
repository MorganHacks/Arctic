using MorganHacks.Applications.Services;
using Npgsql;

namespace MorganHacks.Applications.Data;

public sealed class PostgresEventStore(NpgsqlDataSource dataSource) : IEventStore
{
    public async Task<IReadOnlyList<EventSummary>> ListAsync(CancellationToken ct = default)
    {
        // starts_at first, nulls last, then creation. An event that has not
        // been dated yet is one somebody is still setting up, and it belongs
        // below the one actually being run rather than above it because a
        // column happened to be empty.
        await using var cmd = dataSource.CreateCommand("""
            SELECT id, slug, name, starts_at
              FROM applications.events
             ORDER BY starts_at DESC NULLS LAST, created_at DESC
            """);

        var events = new List<EventSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            events.Add(new EventSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return events;
    }
}
