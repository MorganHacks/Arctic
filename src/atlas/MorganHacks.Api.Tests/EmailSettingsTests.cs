using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Api.Tests;

public class EmailSettingsTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Settings_round_trip_and_revisions_preserve_the_old_settings()
    {
        using var client = await Editor();
        var key = $"settings-{Guid.NewGuid():N}";
        var created = await client.PostAsJsonAsync("/admin/templates", Draft(key, "Preview {{firstName}}", true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Preview {{firstName}}", saved.GetProperty("previewText").GetString());
        Assert.True(saved.GetProperty("clickTracking").GetBoolean());

        var templates = new TemplateStore(db.DataSource);
        var original = (await templates.FindAsync(key))!;
        var rendered = TemplateRenderer.Render(original, new Dictionary<string, string> { ["firstName"] = "Ada" });
        Assert.Contains("Preview Ada</div>", rendered.BodyHtml);
        var revised = await client.PutAsJsonAsync($"/admin/templates/{key}", Draft(key, "", false));
        Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        var current = (await templates.FindAsync(key))!;
        Assert.Null(current.PreviewText);
        Assert.False(current.ClickTracking);
        Assert.NotEqual(original.Id, current.Id);

        await using var read = db.DataSource.CreateCommand("SELECT preview_text, click_tracking FROM notify.templates WHERE id = @id");
        read.Parameters.AddWithValue("id", original.Id);
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Preview {{firstName}}", reader.GetString(0));
        Assert.True(reader.GetBoolean(1));
    }

    [Fact]
    public async Task Preview_length_is_validated_on_save_and_preview()
    {
        using var client = await Editor();
        var text = new string('a', 201);
        var saved = await client.PostAsJsonAsync("/admin/templates", Draft($"settings-{Guid.NewGuid():N}", text, false));
        Assert.Equal(HttpStatusCode.BadRequest, saved.StatusCode);
        var preview = await client.PostAsJsonAsync("/admin/templates/preview", new
        {
            subject = "Subject",
            body = "Body",
            format = "markdown",
            previewText = text,
        });
        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        var valid = await client.PostAsJsonAsync("/admin/templates/preview", new
        {
            subject = "Subject",
            body = "Body",
            format = "markdown",
            previewText = "One <two>",
        });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Contains("One &lt;two&gt;</div>", (await valid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("html").GetString());
    }

    [Fact]
    public async Task Public_clicks_redirect_and_count_without_a_session_or_a_query_destination()
    {
        using var editor = await Editor();
        var key = $"settings-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.Created, (await editor.PostAsJsonAsync("/admin/templates", Draft(key, "Preview", true))).StatusCode);
        var template = (await new TemplateStore(db.DataSource).FindAsync(key))!;
        var queue = new MessageQueue(db.DataSource);
        var messageId = await queue.EnqueueTransactionalAsync(template, "test@example.invalid", null, new Dictionary<string, string>());
        var message = Assert.Single(await queue.ClaimAsync("tracking-test", 10), item => item.Id == messageId);
        Assert.True(message.ClickTracking);
        Assert.Contains("Preview</div>", message.BodyHtml);
        var store = new LinkTrackingStore(db.DataSource);
        var tracked = await store.PrepareAsync(message, "https://api.example.invalid");
        var location = Assert.Single(EmailLinks.Destinations(tracked.BodyHtml));
        var path = new Uri(location).AbsolutePath;
        using var client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
        Assert.Equal(HttpStatusCode.Found, head.StatusCode);
        using var clicked = await client.GetAsync(path + "?url=https://untrusted.invalid");
        Assert.Equal(HttpStatusCode.Found, clicked.StatusCode);
        Assert.Equal("https://example.invalid/go?a=1&b=2", clicked.Headers.Location!.OriginalString);
        Assert.True(clicked.Headers.CacheControl!.NoStore);
        Assert.Equal("no-referrer", Assert.Single(clicked.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/email/click/{Guid.NewGuid():N}")).StatusCode);

        await using var count = db.DataSource.CreateCommand("SELECT click_count FROM notify.tracked_links WHERE message_id = @id");
        count.Parameters.AddWithValue("id", messageId);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    private async Task<HttpClient> Editor()
    {
        var id = await db.AddPersonAsync($"editor-{Guid.NewGuid():N}@example.invalid");
        await db.AddToTeamAsync(id, "comms");
        using var scope = _app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(id);
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");
        return client;
    }

    private static object Draft(string key, string previewText, bool clickTracking) => new
    {
        key,
        kind = "broadcast",
        subject = "Subject",
        format = "html",
        body = "<a href='https://example.invalid/go?a=1&amp;b=2'>Go</a>",
        fromLocal = "mail",
        fromDomain = "example.invalid",
        previewText,
        clickTracking,
    };
}
