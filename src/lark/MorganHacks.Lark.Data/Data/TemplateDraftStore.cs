using System.Text.Json;
using MorganHacks.Lark.Data.Domain;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Lark.Data.Data;

public sealed record WorkingTemplate(TemplateDraft Content, int? BaseVersion, DateTimeOffset UpdatedAt);

public sealed class TemplateDraftStore(NpgsqlDataSource dataSource)
{
    private const string Visible = """
        (NOT EXISTS (SELECT 1 FROM notify.templates t WHERE t.key = d.key)
         OR EXISTS (SELECT 1 FROM notify.templates t WHERE t.key = d.key AND t.superseded_at IS NULL))
        """;

    public async Task<WorkingTemplate?> FindAsync(string key, Guid author, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand($"SELECT content, base_version, updated_at FROM notify.template_working_drafts d WHERE key = @key AND author = @author AND {Visible}");
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("author", author);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<WorkingTemplate>> ListAsync(Guid author, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand($"SELECT content, base_version, updated_at FROM notify.template_working_drafts d WHERE author = @author AND {Visible} ORDER BY updated_at DESC");
        command.Parameters.AddWithValue("author", author);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var drafts = new List<WorkingTemplate>();
        while (await reader.ReadAsync(ct)) drafts.Add(Read(reader));
        return drafts;
    }

    public async Task<WorkingTemplate?> SaveAsync(TemplateDraft draft, Guid author, int? baseVersion, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO notify.template_working_drafts (key, author, content, base_version)
            SELECT @key, @author, @content, @baseVersion
            WHERE NOT EXISTS (SELECT 1 FROM notify.templates WHERE key = @key)
               OR EXISTS (SELECT 1 FROM notify.templates WHERE key = @key AND superseded_at IS NULL)
            ON CONFLICT (key, author) DO UPDATE
            SET content = EXCLUDED.content, updated_at = now()
            RETURNING content, base_version, updated_at
            """);
        command.Parameters.AddWithValue("key", draft.Key);
        command.Parameters.AddWithValue("author", author);
        command.Parameters.AddWithValue("content", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(draft));
        command.Parameters.AddWithValue("baseVersion", NpgsqlDbType.Integer, (object?)baseVersion ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task<bool> DeleteUnpublishedAsync(string key, Guid author, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand("""
            DELETE FROM notify.template_working_drafts
            WHERE key = @key AND author = @author AND base_version IS NULL
              AND NOT EXISTS (SELECT 1 FROM notify.templates WHERE key = @key)
            """);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("author", author);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task DeleteAsync(string key, Guid author, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM notify.template_working_drafts WHERE key = @key AND author = @author");
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("author", author);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static WorkingTemplate Read(NpgsqlDataReader reader) => new(
        JsonSerializer.Deserialize<TemplateDraft>(reader.GetString(0))!,
        reader.IsDBNull(1) ? null : reader.GetInt32(1),
        reader.GetFieldValue<DateTimeOffset>(2));
}
