using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

namespace MorganHacks.Applications.Forms;

public sealed class PostgresFormStore(NpgsqlDataSource dataSource) : IFormStore
{
    /// <summary>
    /// How a form is written to and read from the column.
    /// </summary>
    /// <remarks>
    /// Enums as strings, not numbers. A stored 3 means nothing to anybody
    /// reading the row, and reordering the enum later would silently change
    /// what every existing form says.
    /// </remarks>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string Columns =
        "id, event_id, version, status, fields, created_at, published_at";

    public async Task<FormVersion?> PublishedAsync(Guid eventId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            $"SELECT {Columns} FROM applications.form_versions "
            + "WHERE event_id = @eventId AND status = 'published'");
        cmd.Parameters.AddWithValue("eventId", eventId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task<FormVersion> DraftAsync(
        Guid eventId, Guid? actorId, CancellationToken ct = default)
    {
        await using (var cmd = dataSource.CreateCommand(
            $"SELECT {Columns} FROM applications.form_versions "
            + "WHERE event_id = @eventId AND status = 'draft'"))
        {
            cmd.Parameters.AddWithValue("eventId", eventId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return Read(reader);
            }
        }

        var published = await PublishedAsync(eventId, ct);
        var seed = published?.Fields ?? MlhFields.All;

        const string insert = """
            INSERT INTO applications.form_versions
                (event_id, version, status, fields, created_by)
            VALUES (
                @eventId,
                coalesce((SELECT max(version) FROM applications.form_versions
                           WHERE event_id = @eventId), 0) + 1,
                'draft', @fields::jsonb, @actorId)
            RETURNING id, event_id, version, status, fields, created_at, published_at
            """;

        await using var create = dataSource.CreateCommand(insert);
        create.Parameters.AddWithValue("eventId", eventId);
        create.Parameters.AddWithValue("fields", JsonSerializer.Serialize(seed, Json));
        create.Parameters.AddWithValue("actorId", (object?)actorId ?? DBNull.Value);

        await using var created = await create.ExecuteReaderAsync(ct);
        await created.ReadAsync(ct);
        return Read(created);
    }

    public async Task SaveDraftAsync(
        Guid eventId, IReadOnlyList<FormField> fields, CancellationToken ct = default)
    {
        // Only the draft. A published form is frozen by a trigger as well, so
        // this narrowing is convenience rather than the guarantee.
        await using var cmd = dataSource.CreateCommand(
            "UPDATE applications.form_versions SET fields = @fields::jsonb "
            + "WHERE event_id = @eventId AND status = 'draft'");
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("fields", JsonSerializer.Serialize(fields, Json));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<FormVersion> PublishAsync(
        Guid eventId, Guid? actorId, CancellationToken ct = default)
    {
        var draft = await DraftAsync(eventId, actorId, ct);

        var problems = FormValidation.Check(draft.Fields);
        if (problems.Count > 0)
        {
            // Refused before anything is written. A half-published form is not
            // a state worth having recovery code for.
            throw new FormNotPublishableException(problems);
        }

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Retire first. A unique index allows one published form per event, so
        // doing this the other way round fails on the index rather than
        // swapping cleanly.
        await using (var cmd = new NpgsqlCommand(
            "UPDATE applications.form_versions SET status = 'retired' "
            + "WHERE event_id = @eventId AND status = 'published'", connection, transaction))
        {
            cmd.Parameters.AddWithValue("eventId", eventId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        const string publish = """
            UPDATE applications.form_versions
               SET status = 'published', published_at = now(), published_by = @actorId
             WHERE id = @id
            RETURNING id, event_id, version, status, fields, created_at, published_at
            """;

        FormVersion published;
        await using (var cmd = new NpgsqlCommand(publish, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", draft.Id);
            cmd.Parameters.AddWithValue("actorId", (object?)actorId ?? DBNull.Value);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            published = Read(reader);
        }

        await transaction.CommitAsync(ct);
        return published;
    }

    public async Task<IReadOnlyList<FormVersion>> HistoryAsync(
        Guid eventId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            $"SELECT {Columns} FROM applications.form_versions "
            + "WHERE event_id = @eventId ORDER BY version DESC");
        cmd.Parameters.AddWithValue("eventId", eventId);

        var versions = new List<FormVersion>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            versions.Add(Read(reader));
        }

        return versions;
    }

    private static FormVersion Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetInt32(2),
        reader.GetString(3),
        JsonSerializer.Deserialize<List<FormField>>(reader.GetString(4), Json) ?? [],
        reader.GetFieldValue<DateTimeOffset>(5),
        reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6));
}
