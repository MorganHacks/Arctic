using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;
using NpgsqlTypes;

namespace MorganHacks.Api.Tests;

public class EmailCampaignAnalyticsTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>
{
    [Fact]
    public async Task Ranks_five_broadcasts_by_unique_click_rate_without_counting_links_as_emails()
    {
        var aggregate = await Campaign("Aggregate");
        var first = await Message(aggregate, 3);
        await Link(first, "second", 4);
        await Message(aggregate, 2);
        await Message(aggregate, 0);
        await Message(aggregate, null);
        await Message(aggregate, 200, sent: false);

        var winner = await Campaign("Highest rate");
        for (var i = 0; i < 3; i++) await Message(winner, 1);
        for (var i = 0; i < 5; i++)
        {
            var campaign = await Campaign($"Half clicked {i}");
            await Message(campaign, 1);
            await Message(campaign, 0);
        }
        var untracked = await Campaign("Untracked");
        await Message(untracked, null);
        var test = await Campaign("Test", key: $"test_{Guid.NewGuid():N}");
        await Message(test, 100);
        var transactional = await Campaign("Sign-in", kind: "transactional");
        await Message(transactional, 100);
        var automatic = await Campaign("Automatic", authored: false);
        await Message(automatic, 100);
        var draft = await Campaign("Not sent");
        await Message(draft, 100, sent: false);

        var result = await new EmailAnalyticsStore(db.DataSource).ReadBestCampaignsAsync(false);

        Assert.Equal(5, result.Count);
        Assert.Equal(winner, result[0].Id);
        var row = Assert.Single(result, item => item.Id == aggregate);
        Assert.Equal(4, row.SentEmails);
        Assert.Equal(3, row.TrackedEmails);
        Assert.Equal(2, row.ClickedEmails);
        Assert.Equal(9, row.TotalClicks);
        Assert.All(result, item => Assert.Null(item.PreviewHtml));
        Assert.DoesNotContain(result, item => new[] { untracked, test, transactional, automatic, draft }.Contains(item.Id));
    }

    [Fact]
    public async Task Campaign_metrics_require_statistics_access_and_previews_require_template_access()
    {
        var campaign = await Campaign("Preview permissions");
        await Message(campaign, 1);
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        const string path = "/admin/analytics/email/campaigns";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        var person = await db.AddPersonAsync($"viewer-{Guid.NewGuid():N}@example.test");
        using var scope = app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(person);
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        await db.GrantAsync(person, "email.view_stats");
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@example.test", json);
        Assert.DoesNotContain("private-template-content", json);
        Assert.DoesNotContain("https://example.test", json);
        var metrics = await response.Content.ReadFromJsonAsync<CampaignsResponse>();
        Assert.NotNull(metrics);
        Assert.All(metrics.Campaigns, row => Assert.Null(row.PreviewHtml));
        await db.GrantAsync(person, "email.manage_templates");
        var previews = await client.GetFromJsonAsync<CampaignsResponse>(path);
        Assert.NotNull(previews);
        Assert.Contains(previews.Campaigns, row => row.Id == campaign && row.PreviewHtml == "<p>private-template-content</p>");
    }

    private async Task<Guid> Campaign(string name, string kind = "broadcast", string? key = null, bool authored = true)
    {
        var actor = await db.AddPersonAsync($"author-{Guid.NewGuid():N}@example.test");
        await using var command = db.DataSource.CreateCommand("""
            WITH template AS (
                INSERT INTO notify.templates (key, name, kind, subject, body_html, body_text, from_local, from_domain)
                VALUES (@key, @name, @kind, 'Subject', '<p>private-template-content</p>', 'Body', 'mail', 'example.test')
                RETURNING id
            )
            INSERT INTO notify.campaigns (template_id, name, created_by)
            SELECT id, @name, CASE WHEN @authored THEN @actor END FROM template RETURNING id
            """);
        command.Parameters.AddWithValue("key", key ?? $"campaign-metrics-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("authored", authored);
        command.Parameters.AddWithValue("actor", actor);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> Message(Guid campaign, int? clicks, bool sent = true)
    {
        await using var command = db.DataSource.CreateCommand("""
            INSERT INTO notify.messages (campaign_id, to_email, rendered_subject, rendered_body_html,
                                         rendered_body_text, status, sent_at)
            VALUES (@campaign, @email, 'Subject', '<p>recipient content</p>', 'Body',
                    CASE WHEN @sent THEN 'sent' ELSE 'pending' END,
                    CASE WHEN @sent THEN now() END) RETURNING id
            """);
        command.Parameters.AddWithValue("campaign", campaign);
        command.Parameters.AddWithValue("email", $"recipient-{Guid.NewGuid():N}@example.test");
        command.Parameters.AddWithValue("sent", sent);
        var message = (Guid)(await command.ExecuteScalarAsync())!;
        if (clicks.HasValue) await Link(message, "first", clicks.Value);
        return message;
    }

    private async Task Link(Guid message, string suffix, int clicks)
    {
        var destination = $"https://example.test/{suffix}";
        await using var command = db.DataSource.CreateCommand("""
            INSERT INTO notify.tracked_links (message_id, destination, destination_hash, click_count)
            VALUES (@message, @destination, @hash, @clicks)
            """);
        command.Parameters.AddWithValue("message", message);
        command.Parameters.AddWithValue("destination", destination);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Bytea, SHA256.HashData(Encoding.UTF8.GetBytes(destination)));
        command.Parameters.AddWithValue("clicks", clicks);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record CampaignsResponse(EmailCampaignPerformance[] Campaigns);
}
