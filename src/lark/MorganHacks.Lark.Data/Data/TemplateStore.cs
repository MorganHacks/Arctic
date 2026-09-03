using MorganHacks.Lark.Data.Domain;
using Npgsql;

namespace MorganHacks.Lark.Data.Data;

/// <summary>Reads the templates messages are rendered from.</summary>
public sealed class TemplateStore(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// The template a key currently means.
    /// </summary>
    /// <remarks>
    /// Live versions only. Editing a template retires the old row and writes a
    /// new one — see <see cref="TemplateCatalog"/> and 0017 — so a key has one
    /// live row and however many retired ones behind it. Without the filter
    /// this returns whichever the planner reached first, which is a template
    /// chosen at random from its own history.
    /// <para>
    /// A campaign that was approved against a since-retired version still finds
    /// it: <c>MessageQueue</c> and <c>CampaignStore</c> join on
    /// <c>campaigns.template_id</c>, which points at the exact row. This is the
    /// lookup by name, and by name means the current one.
    /// </para>
    /// </remarks>
    public async Task<EmailTemplate?> FindAsync(string key, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, key, kind, subject, body_html, body_text,
                   from_local, from_domain, reply_to
              FROM notify.templates
             WHERE key = @key AND superseded_at IS NULL
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
