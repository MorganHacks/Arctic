using MorganHacks.Applications.Services;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Applications.Data;

public sealed class PostgresEventStore(NpgsqlDataSource dataSource) : IEventStore
{
    /// <summary>
    /// Every column an editor needs, in one order used everywhere below.
    /// </summary>
    /// <remarks>
    /// Named once because <see cref="Read"/> reads them by position, and a
    /// list that drifts between two queries is a capacity that arrives as a
    /// date on one screen and not the other.
    /// </remarks>
    private const string Columns = """
        id, slug, name, starts_at, ends_at, registration_opens_at,
        registration_closes_at, decisions_announced_at, capacity,
        created_at, created_by
        """;

    // starts_at first, nulls last, then creation. An event that has not been
    // dated yet is one somebody is still setting up, and it belongs below the
    // one actually being run rather than above it because a column happened to
    // be empty.
    private const string NewestFirst =
        "ORDER BY starts_at DESC NULLS LAST, created_at DESC";

    public async Task<IReadOnlyList<EventSummary>> ListAsync(CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand($"""
            SELECT id, slug, name, starts_at
              FROM applications.events
             {NewestFirst}
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

    public async Task<IReadOnlyList<EventDetail>> ListDetailedAsync(
        CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand($"""
            SELECT {Columns}
              FROM applications.events
             {NewestFirst}
            """);

        var events = new List<EventDetail>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            events.Add(Read(reader));
        }

        return events;
    }

    public async Task<EventDetail?> ByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand($"""
            SELECT {Columns}
              FROM applications.events
             WHERE id = @id
            """);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task<EventDetail> CreateAsync(
        string slug, string name, Guid createdBy, CancellationToken ct = default)
    {
        // Two columns and the row's own defaults. Every date and the capacity
        // stay null on purpose: none of them is known on the day somebody
        // decides next year is happening.
        await using var cmd = dataSource.CreateCommand($"""
            INSERT INTO applications.events (slug, name, created_by)
            VALUES (@slug, @name, @createdBy)
            RETURNING {Columns}
            """);
        cmd.Parameters.AddWithValue("slug", slug);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("createdBy", createdBy);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Read(reader);
    }

    public async Task<EventDetail?> UpdateAsync(
        Guid id, EventEdit edit, CancellationToken ct = default)
    {
        // One statement with a CASE per column rather than a SET clause built
        // from whichever fields turned up. Assembling SQL from a request is
        // how a column name ends up coming from a caller, and the shape here
        // is fixed and readable at the cost of six parameters nobody sent.
        //
        // COALESCE for the name, CASE for the rest. They differ because the
        // name has no null to preserve — the column is NOT NULL — while a date
        // that has been un-decided has to be storable.
        await using var cmd = dataSource.CreateCommand($"""
            UPDATE applications.events
               SET name = COALESCE(@name, name),
                   starts_at = CASE WHEN @startsAtSet
                                    THEN @startsAt ELSE starts_at END,
                   ends_at = CASE WHEN @endsAtSet
                                  THEN @endsAt ELSE ends_at END,
                   registration_opens_at = CASE WHEN @opensSet
                                                THEN @opens ELSE registration_opens_at END,
                   registration_closes_at = CASE WHEN @closesSet
                                                 THEN @closes ELSE registration_closes_at END,
                   decisions_announced_at = CASE WHEN @announcedSet
                                                 THEN @announced ELSE decisions_announced_at END,
                   capacity = CASE WHEN @capacitySet
                                   THEN @capacity ELSE capacity END
             WHERE id = @id
            RETURNING {Columns}
            """);

        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text)
        {
            Value = (object?)edit.Name ?? DBNull.Value,
        });

        Add(cmd, "startsAt", edit.StartsAt);
        Add(cmd, "endsAt", edit.EndsAt);
        Add(cmd, "opens", edit.RegistrationOpensAt);
        Add(cmd, "closes", edit.RegistrationClosesAt);
        Add(cmd, "announced", edit.DecisionsAnnouncedAt);
        Add(cmd, "capacity", edit.Capacity);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>
    /// A patched instant, as the flag and the value the CASE above reads.
    /// </summary>
    /// <remarks>
    /// Sent as UTC because Npgsql refuses to write a DateTimeOffset carrying
    /// any other offset to a <c>timestamptz</c>, and it is right to: the column
    /// stores an instant and has nowhere to keep the offset it arrived in. The
    /// conversion is lossless — midnight in New York and 05:00 UTC are one
    /// moment — and doing it here rather than in a handler means every caller
    /// gets it, including one written next year.
    /// <para>
    /// The type is stated rather than inferred because these are often sent as
    /// null, and Npgsql cannot tell a null timestamp from a null anything else.
    /// </para>
    /// </remarks>
    private static void Add(NpgsqlCommand cmd, string name, Patch<DateTimeOffset> patch)
    {
        cmd.Parameters.AddWithValue($"{name}Set", patch.Present);
        cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.TimestampTz)
        {
            Value = patch.Value is { } instant
                ? (object)instant.ToUniversalTime()
                : DBNull.Value,
        });
    }

    /// <summary>The same, for the one field that is a count rather than a time.</summary>
    private static void Add(NpgsqlCommand cmd, string name, Patch<int> patch)
    {
        cmd.Parameters.AddWithValue($"{name}Set", patch.Present);
        cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Integer)
        {
            Value = patch.Value is { } capacity ? (object)capacity : DBNull.Value,
        });
    }

    private static EventDetail Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        Instant(reader, 3),
        Instant(reader, 4),
        Instant(reader, 5),
        Instant(reader, 6),
        Instant(reader, 7),
        reader.IsDBNull(8) ? null : reader.GetInt32(8),
        reader.GetFieldValue<DateTimeOffset>(9),
        reader.IsDBNull(10) ? null : reader.GetGuid(10));

    private static DateTimeOffset? Instant(NpgsqlDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : reader.GetFieldValue<DateTimeOffset>(column);
}
