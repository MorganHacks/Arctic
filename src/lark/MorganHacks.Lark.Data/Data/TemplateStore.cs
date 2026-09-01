using MorganHacks.Lark.Data.Domain;
using Npgsql;

namespace MorganHacks.Lark.Data.Data;

/// <summary>Reads the templates messages are rendered from.</summary>
public sealed class TemplateStore(NpgsqlDataSource dataSource)
{
    public async Task<EmailTemplate?> FindAsync(string key, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, key, kind, subject, body_html, body_text,
                   from_local, from_domain, reply_to
              FROM notify.templates
             WHERE key = @key
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("key", key);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new EmailTemplate(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetString(6), reader.GetString(7),
            await reader.IsDBNullAsync(8, ct) ? null : reader.GetString(8));
    }
}
