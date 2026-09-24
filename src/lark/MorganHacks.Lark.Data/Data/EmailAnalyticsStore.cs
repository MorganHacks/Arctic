using System.Text.Json;
using Npgsql;

namespace MorganHacks.Lark.Data.Data;

public sealed record EmailTemplateClicks(string Name, long Clicks, long ClickedEmails);
public sealed record EmailCampaignPerformance(
    Guid Id, string Name, DateTimeOffset SentAt, long SentEmails, long TrackedEmails,
    long ClickedEmails, long TotalClicks, string? PreviewHtml);
public sealed record EmailAnalytics(
    long SentEmails, long TrackedEmails, long ClickedEmails, long TotalClicks,
    long ClickedRecipients, long TrackingEnabledTemplates, IReadOnlyList<EmailTemplateClicks> Templates);

public sealed class EmailAnalyticsStore(NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<EmailCampaignPerformance>> ReadBestCampaignsAsync(
        bool includePreviews, CancellationToken ct = default)
    {
        const string sql = """
            WITH link_totals AS (
                SELECT message_id, sum(click_count)::bigint AS clicks
                  FROM notify.tracked_links GROUP BY message_id
            ), ranked AS (
                SELECT c.id, c.name, c.template_id, max(m.sent_at) AS sent_at,
                       count(*) AS sent_emails,
                       count(*) FILTER (WHERE l.message_id IS NOT NULL) AS tracked_emails,
                       count(*) FILTER (WHERE l.clicks > 0) AS clicked_emails,
                       COALESCE(sum(l.clicks), 0)::bigint AS total_clicks
                  FROM notify.campaigns c
                  JOIN notify.templates t ON t.id = c.template_id
                  JOIN notify.messages m ON m.campaign_id = c.id
                  LEFT JOIN link_totals l ON l.message_id = m.id
                 WHERE c.created_by IS NOT NULL AND t.kind = 'broadcast'
                   AND t.key !~ '^test_[0-9a-f]{32}$' AND m.sent_at IS NOT NULL
                 GROUP BY c.id, c.name, c.template_id
            )
            SELECT r.id, r.name, r.sent_at, r.sent_emails, r.tracked_emails,
                   r.clicked_emails, r.total_clicks,
                   CASE WHEN @previews THEN t.body_html END AS preview_html
              FROM ranked r JOIN notify.templates t ON t.id = r.template_id
             ORDER BY r.clicked_emails::numeric / NULLIF(r.tracked_emails, 0) DESC NULLS LAST,
                      r.clicked_emails DESC, r.sent_emails DESC, r.sent_at DESC, r.id
             LIMIT 5
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("previews", includePreviews);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var campaigns = new List<EmailCampaignPerformance>();
        while (await reader.ReadAsync(ct))
            campaigns.Add(new EmailCampaignPerformance(reader.GetGuid(0), reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2), reader.GetInt64(3), reader.GetInt64(4),
                reader.GetInt64(5), reader.GetInt64(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return campaigns;
    }

    public async Task<EmailAnalytics> ReadAsync(CancellationToken ct = default)
    {
        const string sql = """
            WITH link_totals AS (
                SELECT message_id, sum(click_count)::bigint AS clicks
                  FROM notify.tracked_links GROUP BY message_id
            ), sent AS MATERIALIZED (
                SELECT m.id, m.to_email, t.key, COALESCE(NULLIF(t.name, ''), t.key) AS name,
                       l.message_id IS NOT NULL AS tracked, COALESCE(l.clicks, 0) AS clicks
                  FROM notify.messages m
                  JOIN notify.campaigns c ON c.id = m.campaign_id
                  JOIN notify.templates t ON t.id = c.template_id
                  LEFT JOIN link_totals l ON l.message_id = m.id
                 WHERE m.sent_at IS NOT NULL AND t.key !~ '^test_[0-9a-f]{32}$'
            )
            SELECT count(*), count(*) FILTER (WHERE tracked), count(*) FILTER (WHERE clicks > 0),
                   COALESCE(sum(clicks), 0)::bigint,
                   count(DISTINCT lower(to_email::text)) FILTER (WHERE clicks > 0),
                   (SELECT count(*) FROM notify.templates WHERE click_tracking AND superseded_at IS NULL),
                   COALESCE((SELECT jsonb_agg(ranked) FROM (
                       SELECT name, sum(clicks)::bigint AS clicks,
                              count(*) FILTER (WHERE clicks > 0) AS "clickedEmails"
                         FROM sent GROUP BY key, name HAVING sum(clicks) > 0
                        ORDER BY sum(clicks) DESC, name LIMIT 6
                   ) ranked), '[]'::jsonb)
              FROM sent
            """;
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new EmailAnalytics(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5),
            JsonSerializer.Deserialize<EmailTemplateClicks[]>(reader.GetString(6), Json) ?? []);
    }
}
