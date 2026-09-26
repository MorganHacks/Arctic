using Npgsql;

namespace MorganHacks.Lark.Data.Data;

public sealed class TemplateVisibilityStore(NpgsqlDataSource dataSource)
{
    private const string Accessible = """
        (EXISTS (SELECT 1 FROM notify.templates t WHERE t.key = requested.key AND t.superseded_at IS NULL)
         OR (EXISTS (SELECT 1 FROM notify.template_working_drafts d WHERE d.key = requested.key AND d.author = @person)
             AND NOT EXISTS (SELECT 1 FROM notify.templates t WHERE t.key = requested.key)))
        """;

    public async Task<IReadOnlyList<string>> ListAsync(Guid person, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT requested.key FROM
                (SELECT template_key AS key FROM notify.hidden_templates WHERE person_id = @person) requested
            WHERE {Accessible}
            ORDER BY requested.key
            """);
        command.Parameters.AddWithValue("person", person);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var keys = new List<string>();
        while (await reader.ReadAsync(ct)) keys.Add(reader.GetString(0));
        return keys;
    }

    public async Task<bool> SetHiddenAsync(Guid person, string[] keys, bool hidden, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var available = new NpgsqlCommand($"""
            SELECT count(*) FROM unnest(@keys) AS requested(key) WHERE {Accessible}
            """, connection, transaction);
        available.Parameters.AddWithValue("person", person);
        available.Parameters.AddWithValue("keys", keys);
        if ((long)(await available.ExecuteScalarAsync(ct))! != keys.Length) return false;

        await using var update = new NpgsqlCommand(hidden ? """
            INSERT INTO notify.hidden_templates (person_id, template_key)
            SELECT @person, key FROM unnest(@keys) AS requested(key)
            ON CONFLICT (person_id, template_key) DO NOTHING
            """ : """
            DELETE FROM notify.hidden_templates WHERE person_id = @person AND template_key = ANY(@keys)
            """, connection, transaction);
        update.Parameters.AddWithValue("person", person);
        update.Parameters.AddWithValue("keys", keys);
        await update.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
