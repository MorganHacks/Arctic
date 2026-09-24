using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

public class TemplateDraftTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
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

    [Theory]
    [InlineData("html", "<p>Placeholder body</p>")]
    [InlineData("markdown", "Placeholder **body**")]
    public async Task Settings_without_a_body_reload_as_a_private_draft_then_publish_from_design(string format, string body)
    {
        using var editor = await Editor();
        using var another = await Editor();
        var response = await editor.PostAsJsonAsync("/admin/templates/settings", Draft(format: format));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        var key = saved.GetProperty("key").GetString()!;
        Assert.StartsWith("template_", key);
        Assert.Equal("Placeholder subject", saved.GetProperty("name").GetString());
        Assert.Equal(0, saved.GetProperty("version").GetInt32());
        Assert.True(saved.GetProperty("settingsComplete").GetBoolean());
        Assert.False(saved.GetProperty("designComplete").GetBoolean());
        Assert.True(saved.GetProperty("hasDraft").GetBoolean());

        var reloaded = await editor.GetFromJsonAsync<JsonElement>($"/admin/templates/{key}?draft=true");
        Assert.Equal("", reloaded.GetProperty("body").GetString());
        Assert.Equal("Preview {{firstName}}", reloaded.GetProperty("previewText").GetString());
        Assert.True(reloaded.GetProperty("clickTracking").GetBoolean());
        Assert.Equal(format, reloaded.GetProperty("format").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync($"/admin/templates/{key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await another.GetAsync($"/admin/templates/{key}?draft=true")).StatusCode);
        Assert.Contains(await Listed(editor, true), row => row.GetProperty("key").GetString() == key);
        Assert.DoesNotContain(await Listed(editor, false), row => row.GetProperty("key").GetString() == key);
        Assert.DoesNotContain(await Listed(another, true), row => row.GetProperty("key").GetString() == key);

        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/admin/templates", Draft(key, format: format))).StatusCode);
        Assert.True((await editor.GetFromJsonAsync<JsonElement>($"/admin/templates/{key}?draft=true")).GetProperty("hasDraft").GetBoolean());
        var published = await editor.PostAsJsonAsync("/admin/templates", Draft(key, body, format));
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var completed = await editor.GetFromJsonAsync<JsonElement>($"/admin/templates/{key}?draft=true");
        Assert.Equal(1, completed.GetProperty("version").GetInt32());
        Assert.Equal(body, completed.GetProperty("body").GetString());
        Assert.True(completed.GetProperty("designComplete").GetBoolean());
        Assert.False(completed.GetProperty("hasDraft").GetBoolean());
        Assert.Contains(await Listed(editor, false), row => row.GetProperty("key").GetString() == key);
        await using var count = db.DataSource.CreateCommand("SELECT count(*) FROM notify.template_working_drafts WHERE key = @key");
        count.Parameters.AddWithValue("key", key);
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Settings_do_not_change_a_live_template_and_stale_design_cannot_overwrite_a_new_version()
    {
        using var editor = await Editor();
        using var another = await Editor();
        var key = $"draft-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.Created, (await editor.PostAsJsonAsync("/admin/templates", Draft(key, "Original body"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.PostAsJsonAsync("/admin/templates/settings", Draft(key, subject: "Draft subject"))).StatusCode);
        var live = await editor.GetFromJsonAsync<JsonElement>($"/admin/templates/{key}");
        Assert.Equal("Placeholder subject", live.GetProperty("subject").GetString());
        Assert.Equal("Original body", live.GetProperty("body").GetString());
        Assert.Equal(1, live.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PutAsJsonAsync($"/admin/templates/{key}", Draft(key))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await another.PutAsJsonAsync($"/admin/templates/{key}", Draft(key, "Newer body"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PutAsJsonAsync($"/admin/templates/{key}", Draft(key, "Stale body"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync("/admin/templates/settings", Draft(key))).StatusCode);
        var latest = await editor.GetFromJsonAsync<JsonElement>($"/admin/templates/{key}");
        Assert.Equal("Newer body", latest.GetProperty("body").GetString());
        Assert.Equal(2, latest.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await another.DeleteAsync($"/admin/templates/{key}/draft")).StatusCode);
        Assert.True((await editor.GetFromJsonAsync<JsonElement>($"/admin/templates/{key}?draft=true")).GetProperty("hasDraft").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await editor.DeleteAsync($"/admin/templates/{key}/draft")).StatusCode);
        var reopened = await editor.GetFromJsonAsync<JsonElement>($"/admin/templates/{key}?draft=true");
        Assert.False(reopened.GetProperty("hasDraft").GetBoolean());
        Assert.Equal("Newer body", reopened.GetProperty("body").GetString());
        Assert.Equal(HttpStatusCode.OK, (await editor.PostAsJsonAsync("/admin/templates/settings", Draft(key))).StatusCode);
    }

    [Fact]
    public async Task Completing_design_revises_the_live_template_and_clears_the_working_draft()
    {
        using var editor = await Editor();
        var key = $"draft-{Guid.NewGuid():N}";
        await editor.PostAsJsonAsync("/admin/templates", Draft(key, "Original body"));
        await editor.PostAsJsonAsync("/admin/templates/settings", Draft(key, subject: "Revised subject"));
        var saved = await editor.PutAsJsonAsync($"/admin/templates/{key}", Draft(key, "Revised body", subject: "Revised subject"));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var reloaded = await editor.GetFromJsonAsync<JsonElement>($"/admin/templates/{key}?draft=true");
        Assert.Equal("Revised subject", reloaded.GetProperty("subject").GetString());
        Assert.Equal("Revised body", reloaded.GetProperty("body").GetString());
        Assert.Equal(2, reloaded.GetProperty("version").GetInt32());
        Assert.False(reloaded.GetProperty("hasDraft").GetBoolean());
    }

    [Fact]
    public async Task Template_previews_are_only_included_when_the_list_requests_them()
    {
        using var editor = await Editor();
        var key = $"preview-{Guid.NewGuid():N}";
        await editor.PostAsJsonAsync("/admin/templates", Draft(key, "Original preview body"));

        var ordinary = Assert.Single(await Listed(editor, false), row => row.GetProperty("key").GetString() == key);
        Assert.False(ordinary.TryGetProperty("previewHtml", out _));

        var previewed = Assert.Single(await Listed(editor, false, true), row => row.GetProperty("key").GetString() == key);
        Assert.Contains("Original preview body", previewed.GetProperty("previewHtml").GetString());

        await editor.PostAsJsonAsync("/admin/templates/settings", Draft(key, subject: "Draft subject"));
        var drafted = Assert.Single(await Listed(editor, true, true), row => row.GetProperty("key").GetString() == key);
        Assert.True(drafted.GetProperty("hasDraft").GetBoolean());
        Assert.Contains("Original preview body", drafted.GetProperty("previewHtml").GetString());
    }

    [Theory]
    [InlineData("", "Sender")]
    [InlineData("Subject", "")]
    public async Task Settings_still_require_a_subject_and_sender(string subject, string sender)
    {
        using var editor = await Editor();
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/admin/templates/settings", Draft(subject: subject, sender: sender))).StatusCode);
    }

    [Fact]
    public async Task Saving_drafts_requires_template_permission()
    {
        using var anonymous = _app.CreateClient();
        using var unprivileged = await Editor(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/admin/templates/settings", Draft())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivileged.PostAsJsonAsync("/admin/templates/settings", Draft())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/admin/templates/magic_link/draft")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivileged.DeleteAsync("/admin/templates/magic_link/draft")).StatusCode);
    }

    private static object Draft(string? key = null, string body = "", string format = "markdown", string subject = "Placeholder subject", string sender = "Sender") => new
    {
        key,
        name = "",
        kind = "broadcast",
        subject,
        format,
        body,
        fromName = sender,
        fromLocal = "mail",
        fromDomain = "example.invalid",
        previewText = "Preview {{firstName}}",
        clickTracking = true,
    };

    private static async Task<JsonElement[]> Listed(HttpClient client, bool includeDrafts, bool includePreviews = false) =>
        (await client.GetFromJsonAsync<JsonElement>($"/admin/templates?includeDrafts={includeDrafts.ToString().ToLowerInvariant()}&includePreviews={includePreviews.ToString().ToLowerInvariant()}"))
            .GetProperty("templates").EnumerateArray().ToArray();

    private async Task<HttpClient> Editor(bool grant = true)
    {
        var id = await db.AddPersonAsync($"editor-{Guid.NewGuid():N}@example.invalid");
        if (grant) await db.AddToTeamAsync(id, "comms");
        using var scope = _app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(id);
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");
        return client;
    }
}
