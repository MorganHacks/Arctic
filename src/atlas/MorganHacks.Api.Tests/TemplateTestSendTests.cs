using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api.Tests;

public class TemplateTestSendTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("html", "<p>Hello {{email}} and {{firstName}}</p><script>alert(1)</script>")]
    [InlineData("markdown", "Hello **{{email}}** and {{firstName}}")]
    public async Task Tests_queue_the_unsaved_design_once_without_publishing_a_template(string format, string body)
    {
        using var editor = await Editor();
        var requestId = Guid.NewGuid();
        var key = $"unpublished-{Guid.NewGuid():N}";
        var request = new { requestId, recipient = "test@example.invalid", draft = Draft(key, body, format) };
        var responses = await Task.WhenAll(editor.PostAsJsonAsync("/admin/templates/test", request), editor.PostAsJsonAsync("/admin/templates/test", request));
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(requestId, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync($"/admin/templates/{key}")).StatusCode);
        await using var read = db.DataSource.CreateCommand("""
            SELECT m.rendered_subject, m.rendered_body_html, m.rendered_body_text, m.to_email,
                   t.superseded_at, t.click_tracking, c.recipient_count, m.priority
            FROM notify.messages m JOIN notify.campaigns c ON c.id = m.campaign_id
            JOIN notify.templates t ON t.id = c.template_id WHERE m.id = @id
            """);
        read.Parameters.AddWithValue("id", requestId);
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("[Test] Placeholder subject", reader.GetString(0));
        Assert.Contains("Preview test@example.invalid</div>", reader.GetString(1));
        Assert.Contains("{{firstName}}", reader.GetString(1));
        Assert.DoesNotContain("<script", reader.GetString(1));
        Assert.Contains("test@example.invalid", reader.GetString(2));
        Assert.Equal("test@example.invalid", reader.GetString(3));
        Assert.False(reader.IsDBNull(4));
        Assert.False(reader.GetBoolean(5));
        Assert.Equal(1, reader.GetInt32(6));
        Assert.Equal(10, reader.GetInt16(7));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Test_sending_needs_both_template_and_sending_permissions()
    {
        using var anonymous = _app.CreateClient();
        using var writer = await Editor(send: false);
        using var sender = await Editor(manage: false);
        var request = new { requestId = Guid.NewGuid(), recipient = "test@example.invalid", draft = Draft("test", "Body") };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/admin/templates/test", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await writer.PostAsJsonAsync("/admin/templates/test", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sender.PostAsJsonAsync("/admin/templates/test", request)).StatusCode);
    }

    [Theory]
    [InlineData("invalid", "Body")]
    [InlineData("test@example.invalid", "")]
    [InlineData("test@example.invalid,second@example.invalid", "Body")]
    [InlineData("test@example.invalid\r\nBcc:other@example.invalid", "Body")]
    public async Task Invalid_recipient_or_empty_body_cannot_queue_a_test(string recipient, string body)
    {
        using var editor = await Editor();
        var response = await editor.PostAsJsonAsync("/admin/templates/test", new { requestId = Guid.NewGuid(), recipient, draft = Draft("test", body) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Suppressed_addresses_cannot_receive_a_test()
    {
        using var editor = await Editor();
        var recipient = $"suppressed-{Guid.NewGuid():N}@example.invalid";
        await new MessageQueue(db.DataSource).SuppressAsync(recipient, "hard_bounce");
        var response = await editor.PostAsJsonAsync("/admin/templates/test", new { requestId = Guid.NewGuid(), recipient, draft = Draft("test", "Body") });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static object Draft(string key, string body, string format = "markdown") => new
    {
        key,
        name = "Test fixture",
        kind = "broadcast",
        subject = "Placeholder subject",
        body,
        format,
        fromLocal = "mail",
        fromDomain = "example.invalid",
        fromName = "Sender",
        previewText = "Preview {{email}}",
        clickTracking = true,
    };

    private async Task<HttpClient> Editor(bool manage = true, bool send = true)
    {
        var id = await db.AddPersonAsync($"editor-{Guid.NewGuid():N}@example.invalid");
        if (manage) await db.GrantAsync(id, "email.manage_templates");
        if (send) await db.GrantAsync(id, "email.send_templated");
        using var scope = _app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(id);
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");
        return client;
    }
}
