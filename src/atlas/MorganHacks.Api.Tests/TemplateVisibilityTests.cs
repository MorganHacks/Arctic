using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

public class TemplateVisibilityTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private const string VisibilityUrl = "/admin/templates/preferences/visibility";
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
    public async Task Hidden_templates_follow_the_account_across_sessions_without_affecting_other_people()
    {
        var person = await Person();
        var otherPerson = await Person();
        using var firstSession = await Client(person);
        using var secondSession = await Client(person);
        using var otherAccount = await Client(otherPerson);
        var key = await Create(firstSession);

        var response = await firstSession.PutAsJsonAsync(VisibilityUrl, new { keys = new[] { key }, hidden = true, personId = otherPerson });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(key, await Hidden(secondSession));
        Assert.DoesNotContain(key, await Hidden(otherAccount));
        Assert.Contains((await List(secondSession)).GetProperty("templates").EnumerateArray(),
            template => template.GetProperty("key").GetString() == key);
        Assert.Equal(HttpStatusCode.OK, (await secondSession.GetAsync($"/admin/templates/{key}")).StatusCode);

        await using var stored = db.DataSource.CreateCommand("SELECT person_id FROM notify.hidden_templates WHERE template_key = @key");
        stored.Parameters.AddWithValue("key", key);
        Assert.Equal(person, await stored.ExecuteScalarAsync());

        Assert.Equal(HttpStatusCode.OK, (await Set(secondSession, [key], false)).StatusCode);
        Assert.DoesNotContain(key, await Hidden(firstSession));
    }

    [Fact]
    public async Task Batch_changes_are_idempotent_and_only_change_the_requested_keys()
    {
        using var client = await Client(await Person());
        var first = await Create(client);
        var second = await Create(client);
        var third = await Create(client);
        Assert.Equal(HttpStatusCode.OK, (await Set(client, [first, second, first], true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Set(client, [first, second], true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Set(client, [third], true)).StatusCode);
        Assert.Equal(3, (await Hidden(client)).Length);
        Assert.Equal(HttpStatusCode.OK, (await Set(client, [first, second], false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Set(client, [first, second], false)).StatusCode);
        Assert.Equal(third, Assert.Single(await Hidden(client)));
    }

    [Fact]
    public async Task Separate_sessions_do_not_overwrite_each_others_hidden_keys()
    {
        var person = await Person();
        using var firstSession = await Client(person);
        using var secondSession = await Client(person);
        var first = await Create(firstSession);
        var second = await Create(firstSession);
        var responses = await Task.WhenAll(Set(firstSession, [first], true), Set(secondSession, [second], true));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(new[] { first, second }.Order(), (await Hidden(firstSession)).Order());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unavailable_key_rejects_the_entire_batch(bool hidden)
    {
        using var client = await Client(await Person());
        var key = await Create(client);
        if (!hidden) Assert.Equal(HttpStatusCode.OK, (await Set(client, [key], true)).StatusCode);
        var missing = $"missing-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.NotFound, (await Set(client, [key, missing], hidden)).StatusCode);
        Assert.Equal(!hidden, (await Hidden(client)).Contains(key));
    }

    [Fact]
    public async Task Private_drafts_can_only_be_hidden_by_the_author_and_stay_hidden_after_publishing()
    {
        using var author = await Client(await Person());
        using var other = await Client(await Person());
        var key = $"visibility-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.OK, (await author.PostAsJsonAsync("/admin/templates/settings", Template(key, ""))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Set(other, [key], true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Set(author, [key], true)).StatusCode);
        Assert.Contains(key, await Hidden(author));
        Assert.DoesNotContain(key, await Hidden(other));
        Assert.Empty((await List(author, false)).GetProperty("hiddenKeys").EnumerateArray());
        Assert.Equal(HttpStatusCode.Created, (await author.PostAsJsonAsync("/admin/templates", Template(key))).StatusCode);
        Assert.Contains(key, await Hidden(author));
        Assert.DoesNotContain(key, await Hidden(other));
    }

    [Fact]
    public async Task Revising_keeps_the_preference_and_retiring_removes_it_from_the_gallery_response()
    {
        using var client = await Client(await Person());
        var key = await Create(client);
        Assert.Equal(HttpStatusCode.OK, (await Set(client, [key], true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/admin/templates/{key}", Template(key, "Revised body"))).StatusCode);
        Assert.Contains(key, await Hidden(client));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/admin/templates/{key}?version=2")).StatusCode);
        Assert.DoesNotContain(key, await Hidden(client));
        Assert.Equal(HttpStatusCode.NotFound, (await Set(client, [key], true)).StatusCode);
    }

    [Theory]
    [InlineData("email.manage_templates")]
    [InlineData("email.delete_templates")]
    public async Task Either_gallery_permission_can_save_personal_visibility(string permission)
    {
        using var editor = await Client(await Person());
        var key = await Create(editor);
        var person = await Person(false);
        await db.GrantAsync(person, permission);
        using var client = await Client(person);
        Assert.Equal(HttpStatusCode.OK, (await Set(client, [key], true)).StatusCode);
        Assert.Contains(key, await Hidden(client));
        Assert.Equal(HttpStatusCode.OK, (await Set(client, [key], false)).StatusCode);
    }

    [Fact]
    public async Task Visibility_requires_an_authenticated_account_with_gallery_permission()
    {
        using var anonymous = _app.CreateClient();
        using var unprivileged = await Client(await Person(false));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Set(anonymous, ["magic_link"], true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Set(unprivileged, ["magic_link"], true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsync(VisibilityUrl, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivileged.PutAsync(VisibilityUrl, null)).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"keys\":[],\"hidden\":true}")]
    [InlineData("{\"keys\":[\"magic_link\"]}")]
    [InlineData("{\"keys\":[null],\"hidden\":true}")]
    [InlineData("{\"keys\":[\"\"],\"hidden\":true}")]
    [InlineData("{\"keys\":[\"invalid/key\"],\"hidden\":true}")]
    public async Task Invalid_visibility_requests_are_rejected(string body)
    {
        using var client = await Client(await Person());
        var response = await client.PutAsync(VisibilityUrl, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await Hidden(client));
    }

    [Fact]
    public async Task Visibility_limits_batch_and_key_sizes()
    {
        using var client = await Client(await Person());
        Assert.Equal(HttpStatusCode.BadRequest, (await Set(client, Enumerable.Repeat("magic_link", 201).ToArray(), true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Set(client, [new string('a', 65)], true)).StatusCode);
        Assert.Empty(await Hidden(client));
    }

    private static Task<HttpResponseMessage> Set(HttpClient client, string[] keys, bool hidden) =>
        client.PutAsJsonAsync(VisibilityUrl, new { keys, hidden });

    private static async Task<JsonElement> List(HttpClient client, bool drafts = true) =>
        await client.GetFromJsonAsync<JsonElement>($"/admin/templates?includeDrafts={drafts.ToString().ToLowerInvariant()}");

    private static async Task<string[]> Hidden(HttpClient client) =>
        (await List(client)).GetProperty("hiddenKeys").EnumerateArray().Select(key => key.GetString()!).ToArray();

    private static object Template(string key, string body = "Template body") => new
    {
        key,
        kind = "broadcast",
        subject = "Template subject",
        format = "markdown",
        body,
        fromName = "Sender",
        fromLocal = "mail",
        fromDomain = "example.invalid",
    };

    private static async Task<string> Create(HttpClient client)
    {
        var key = $"visibility-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/admin/templates", Template(key))).StatusCode);
        return key;
    }

    private async Task<Guid> Person(bool grant = true)
    {
        var id = await db.AddPersonAsync($"visibility-{Guid.NewGuid():N}@example.invalid");
        if (grant) await db.AddToTeamAsync(id, "comms");
        return id;
    }

    private async Task<HttpClient> Client(Guid person)
    {
        using var scope = _app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(person);
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");
        return client;
    }
}
