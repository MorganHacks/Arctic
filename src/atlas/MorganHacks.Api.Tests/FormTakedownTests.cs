using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Applications.Forms;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Taking a live form down, and saying when it should close on its own.
/// </summary>
/// <remarks>
/// Both end with a form nobody can fill in, and they are not the same thing. A
/// deadline is a moment the form chooses; unpublishing is somebody deciding
/// now. The pair worth watching is that neither destroys anything: a form that
/// stops accepting answers still has the answers it already has, and still has
/// the version that describes what those answers were given to. Losing that
/// would make the responses screen lie about questions rather than merely be
/// empty.
/// </remarks>
public class FormTakedownTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Unpublishing_stops_the_public_page_serving()
    {
        var (cookie, form, code) = await PublishedFormAsync();

        Assert.Equal(HttpStatusCode.OK, (await Public($"/forms/{code}")).StatusCode);

        var taken = await Send(HttpMethod.Post, $"/admin/forms/{form}/unpublish", cookie);
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);

        // The same answer an unknown code gets. An unpublished form that said
        // so would be a way to find out which codes are real.
        Assert.Equal(HttpStatusCode.NotFound, (await Public($"/forms/{code}")).StatusCode);
    }

    [Fact]
    public async Task The_answers_and_the_history_survive_being_taken_down()
    {
        var (cookie, form, code) = await PublishedFormAsync();
        await Send(HttpMethod.Post, $"/admin/forms/{form}/unpublish", cookie);

        // The point of the whole thing. A form nobody can fill in is not a form
        // whose answers have gone.
        var history = await Send(HttpMethod.Get, $"/admin/forms/{form}/versions", cookie);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Contains("\"version\"", await history.Content.ReadAsStringAsync());

        // And it can still be edited, because it is a draft again.
        Assert.Equal(
            HttpStatusCode.OK,
            (await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie)).StatusCode);
    }

    [Fact]
    public async Task Publishing_again_brings_it_back()
    {
        var (cookie, form, code) = await PublishedFormAsync();
        await Send(HttpMethod.Post, $"/admin/forms/{form}/unpublish", cookie);

        var again = await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Public($"/forms/{code}")).StatusCode);
    }

    [Fact]
    public async Task Taking_down_a_form_that_is_not_up_is_refused_rather_than_pretended()
    {
        var cookie = await OrganizerAsync(
            Permission.FormsManage.Value, Permission.ApplicationsView.Value);
        var form = await CreateFormAsync(cookie, await db.AddEventAsync());

        // Never published. Answering OK would tell somebody they had taken
        // down a form that was never up.
        var response = await Send(HttpMethod.Post, $"/admin/forms/{form}/unpublish", cookie);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_deadline_can_be_set_and_cleared()
    {
        var (cookie, form, _) = await PublishedFormAsync();
        var closes = DateTimeOffset.UtcNow.AddDays(7);

        var set = await Send(
            HttpMethod.Put, $"/admin/forms/{form}/schedule", cookie, new { closesAt = closes });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Contains("closesAt", await set.Content.ReadAsStringAsync());

        // Null is a form that stays open until somebody takes it down, which is
        // a different state from one whose deadline has passed.
        var cleared = await Send(
            HttpMethod.Put, $"/admin/forms/{form}/schedule", cookie,
            new { closesAt = (DateTimeOffset?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
    }

    [Fact]
    public async Task A_deadline_in_the_past_closes_the_form_to_the_public()
    {
        var (cookie, form, code) = await PublishedFormAsync();

        await Send(
            HttpMethod.Put, $"/admin/forms/{form}/schedule", cookie,
            new { closesAt = DateTimeOffset.UtcNow.AddMinutes(-1) });

        // Still found, unlike an unpublished one: somebody sent this link
        // yesterday deserves to be told it closed rather than that it never
        // existed.
        var page = await Public($"/forms/{code}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("\"open\":false", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_closed_form_refuses_a_submission_rather_than_only_hiding_it()
    {
        // The failure this exists for: somebody with the page already open
        // posts after the deadline. Hiding the questions is not a gate.
        var (cookie, form, code) = await PublishedFormAsync();

        await Send(
            HttpMethod.Put, $"/admin/forms/{form}/schedule", cookie,
            new { closesAt = DateTimeOffset.UtcNow.AddMinutes(-1) });

        var submitted = await _app.CreateClient().PostAsJsonAsync(
            $"/forms/{code}/submit", new { answers = new Dictionary<string, string>() });

        Assert.NotEqual(HttpStatusCode.OK, submitted.StatusCode);
    }

    [Fact]
    public async Task Both_routes_need_forms_manage()
    {
        var (owner, form, _) = await PublishedFormAsync();
        var reader = await OrganizerAsync(Permission.ApplicationsView.Value);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await Send(HttpMethod.Post, $"/admin/forms/{form}/unpublish", reader)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await Send(HttpMethod.Put, $"/admin/forms/{form}/schedule", reader,
                new { closesAt = (DateTimeOffset?)null })).StatusCode);
    }

    // ---------------------------------------------------------------- setup ---

    private async Task<(string Cookie, Guid Form, string Code)> PublishedFormAsync()
    {
        var cookie = await OrganizerAsync(
            Permission.FormsManage.Value, Permission.ApplicationsView.Value);
        var form = await CreateFormAsync(cookie, await db.AddEventAsync());

        await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie);
        var published = await Send(HttpMethod.Post, $"/admin/forms/{form}/publish", cookie);
        published.EnsureSuccessStatusCode();

        return (cookie, form, await CodeOfAsync(cookie, form));
    }

    private async Task<string> CodeOfAsync(string cookie, Guid form)
    {
        var response = await Send(HttpMethod.Get, $"/admin/forms/{form}/draft", cookie);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("form").GetProperty("code").GetString()!;
    }

    private async Task<Guid> CreateFormAsync(string cookie, Guid eventId)
    {
        var response = await Send(HttpMethod.Post, $"/admin/forms?eventId={eventId}", cookie,
            new { name = "Application", kind = "application" });
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<string> OrganizerAsync(params string[] permissions)
    {
        var id = await db.AddPersonAsync($"takedown-{Guid.NewGuid():N}@example.com");
        foreach (var permission in permissions)
        {
            await db.GrantAsync(id, permission);
        }

        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(id)}";
    }

    private Task<HttpResponseMessage> Public(string path) =>
        _app.CreateClient().GetAsync(path);

    private Task<HttpResponseMessage> Send(
        HttpMethod method, string path, string cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        }).SendAsync(request);
    }
}
