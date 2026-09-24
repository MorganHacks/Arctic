using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;
using NpgsqlTypes;

namespace MorganHacks.Api.Tests;

public class EmailAnalyticsTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>
{
    [Fact]
    public async Task Aggregates_clicks_without_double_counting_emails_or_including_tests_and_unsent_mail()
    {
        var template = await Template($"analytics-{Guid.NewGuid():N}", false);
        var first = await Message(template, "one@example.test");
        await Link(first, "https://example.test/a", 3);
        await Link(first, "https://example.test/b", 4);
        var second = await Message(template, "ONE@example.test");
        await Link(second, "https://example.test/a", 2);
        await Link(await Message(template, "other@example.test"), "https://example.test/a", 0);
        await Message(template, "untracked@example.test");
        await Link(await Message(template, "pending@example.test", false), "https://example.test/a", 20);
        var testTemplate = await Template($"test_{Guid.NewGuid():N}", false);
        await Link(await Message(testTemplate, "test@example.test"), "https://example.test/a", 50);
        await Template($"enabled-{Guid.NewGuid():N}", true);

        var result = await new EmailAnalyticsStore(db.DataSource).ReadAsync();

        Assert.Equal(4, result.SentEmails);
        Assert.Equal(3, result.TrackedEmails);
        Assert.Equal(2, result.ClickedEmails);
        Assert.Equal(9, result.TotalClicks);
        Assert.Equal(1, result.ClickedRecipients);
        Assert.Equal(1, result.TrackingEnabledTemplates);
        var top = Assert.Single(result.Templates);
        Assert.Equal("Newsletter", top.Name);
        Assert.Equal(9, top.Clicks);
        Assert.Equal(2, top.ClickedEmails);
    }

    [Fact]
    public async Task Email_statistics_require_their_own_permission_and_do_not_expose_recipients()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/analytics/email")).StatusCode);
        var person = await db.AddPersonAsync($"analytics-{Guid.NewGuid():N}@example.test");
        await db.GrantAsync(person, "applications.view");
        using var scope = app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(person);
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/analytics/email")).StatusCode);
        await db.GrantAsync(person, "email.view_stats");
        using var response = await client.GetAsync("/admin/analytics/email");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@example.test", json);
        Assert.DoesNotContain("https://example.test", json);
    }

    private async Task<Guid> Template(string key, bool tracking)
    {
        await using var command = db.DataSource.CreateCommand("""
            INSERT INTO notify.templates (key, name, kind, subject, body_html, body_text,
                                          from_local, from_domain, click_tracking)
            VALUES (@key, 'Newsletter', 'broadcast', 'Subject', '<p>Body</p>', 'Body',
                    'mail', 'example.test', @tracking) RETURNING id
            """);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("tracking", tracking);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> Message(Guid template, string email, bool sent = true)
    {
        await using var command = db.DataSource.CreateCommand("""
            WITH campaign AS (
                INSERT INTO notify.campaigns (template_id, name)
                VALUES (@template, 'Analytics test') RETURNING id
            )
            INSERT INTO notify.messages (campaign_id, to_email, rendered_subject, rendered_body_html,
                                         rendered_body_text, status, sent_at)
            SELECT id, @email, 'Subject', '<p>Body</p>', 'Body',
                   CASE WHEN @sent THEN 'sent' ELSE 'pending' END,
                   CASE WHEN @sent THEN now() END FROM campaign RETURNING id
            """);
        command.Parameters.AddWithValue("template", template);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("sent", sent);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task Link(Guid message, string destination, int clicks)
    {
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
}
