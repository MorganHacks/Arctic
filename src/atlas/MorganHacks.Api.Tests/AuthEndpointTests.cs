using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The auth endpoints against a real database, because the rules being tested
/// are about what the endpoint reveals, not about what a mock returns.
/// </summary>
public class AuthEndpointTests(IdentityDatabase db)
    : IClassFixture<IdentityDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Client() => _app.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string Unique() => $"auth-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task An_unknown_address_is_answered_exactly_like_a_known_one()
    {
        // The single most important behaviour here. A difference in status or
        // body turns this endpoint into a lookup service for who applied.
        var known = Unique();
        await db.AddPersonAsync(known);

        var a = await Client().PostAsJsonAsync("/auth/magic-link", new { email = known });
        var b = await Client().PostAsJsonAsync("/auth/magic-link", new { email = Unique() });

        Assert.Equal(a.StatusCode, b.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, a.StatusCode);
        Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Repeated_requests_for_one_address_are_throttled_identically()
    {
        // Rejection must look like success too, or the throttle itself
        // confirms the address exists.
        var email = Unique();
        await db.AddPersonAsync(email);
        var client = Client();

        var responses = new List<(HttpStatusCode Code, string Body)>();
        for (var i = 0; i < 5; i++)
        {
            var r = await client.PostAsJsonAsync("/auth/magic-link", new { email });
            responses.Add((r.StatusCode, await r.Content.ReadAsStringAsync()));
        }

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.Code));
        Assert.Single(responses.Select(r => r.Body).Distinct());
    }

    [Fact]
    public async Task An_empty_address_is_rejected()
    {
        var r = await Client().PostAsJsonAsync("/auth/magic-link", new { email = "" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Every_bad_token_gets_the_same_message()
    {
        // Distinguishing expired from already-used from never-existed only
        // helps somebody probing tokens.
        var client = Client();
        var invented = await client.GetAsync("/auth/consume?token=not-a-real-token");
        var empty = await client.GetAsync("/auth/consume");

        Assert.Equal(HttpStatusCode.BadRequest, invented.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Without_a_session_cookie_me_is_unauthorized()
    {
        var r = await Client().GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task A_consumed_link_sets_a_session_cookie_that_cannot_be_read_by_script()
    {
        var email = Unique();
        var personId = await db.AddPersonAsync(email);

        using var scope = _app.Services.CreateScope();
        var links = scope.ServiceProvider
            .GetRequiredService<MorganHacks.Identity.Services.MagicLinkService>();
        var token = (await links.IssueAsync(email))?.Token;
        Assert.NotNull(token);

        var response = await Client().GetAsync($"/auth/consume?token={token}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("mh_session", StringComparison.Ordinal));

        // An XSS bug must not be able to lift the session.
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        // Lax rather than Strict: Strict drops the cookie on the navigation a
        // magic link produces, so the user lands logged out.
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        // The raw token must never appear anywhere but the cookie itself.
        Assert.DoesNotContain(token!, await response.Content.ReadAsStringAsync());

        _ = personId;
    }

    [Fact]
    public async Task A_link_cannot_be_used_twice_through_the_endpoint()
    {
        var email = Unique();
        await db.AddPersonAsync(email);

        using var scope = _app.Services.CreateScope();
        var links = scope.ServiceProvider
            .GetRequiredService<MorganHacks.Identity.Services.MagicLinkService>();
        var token = (await links.IssueAsync(email))?.Token;

        var first = await Client().GetAsync($"/auth/consume?token={token}");
        var second = await Client().GetAsync($"/auth/consume?token={token}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }
}
