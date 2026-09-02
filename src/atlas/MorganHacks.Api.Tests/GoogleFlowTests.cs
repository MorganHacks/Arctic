using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The parts of the OAuth flow that are ours: PKCE, state, and what happens
/// when any of it is missing. The token exchange itself belongs to Google.
/// </summary>
public class GoogleFlowTests(IdentityDatabase db)
    : IClassFixture<IdentityDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            b.UseSetting("Google:ClientId", "test-client-id.apps.googleusercontent.com");
            b.UseSetting("Google:ClientSecret", "test-secret");
            b.UseSetting("Google:RedirectUri", "https://example.test/auth/google/callback");
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    // AllowAutoRedirect off: the redirect to Google is the thing under test.
    private HttpClient Client() => _app.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
    });

    [Fact]
    public async Task Starting_sign_in_redirects_to_google_with_pkce()
    {
        var response = await Client().GetAsync("/auth/google/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", location);
        Assert.Contains("code_challenge_method=S256", location);
        Assert.Contains("response_type=code", location);
        Assert.Contains("state=", location);

        // The verifier must never travel to Google; only its hash may.
        var verifier = CookieValue(response, "mh_oidc_verifier");
        Assert.NotNull(verifier);
        Assert.DoesNotContain(Uri.EscapeDataString(verifier!), location);
    }

    [Fact]
    public async Task The_pkce_and_state_cookies_cannot_be_read_by_script()
    {
        var response = await Client().GetAsync("/auth/google/");

        foreach (var name in new[] { "mh_oidc_state", "mh_oidc_verifier" })
        {
            var cookie = response.Headers.GetValues("Set-Cookie")
                .Single(c => c.StartsWith(name, StringComparison.Ordinal));
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_state_cookies_are_scoped_where_the_browser_will_be()
    {
        // Not to this service's own route. The console serves the API from its
        // own origin under /api, so the browser lands on /api/auth/google/...
        // and a cookie scoped to /auth/google is never sent there — the
        // callback then finds no state and refuses a sign-in that was fine.
        var start = await Client().GetAsync("/auth/google/");

        var cookies = start.Headers.GetValues("Set-Cookie").ToList();

        // The test host configures the redirect as https://example.test/auth/google/callback.
        Assert.All(cookies, c => Assert.Contains("path=/auth/google", c, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_callback_with_no_state_cookie_is_refused()
    {
        // A code delivered to a browser that never started the flow.
        var r = await Client().GetAsync("/auth/google/callback?code=abc&state=xyz");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task A_callback_whose_state_does_not_match_is_refused()
    {
        var start = await Client().GetAsync("/auth/google/");
        var state = CookieValue(start, "mh_oidc_state")!;
        var verifier = CookieValue(start, "mh_oidc_verifier")!;

        var request = new HttpRequestMessage(
            HttpMethod.Get, "/auth/google/callback?code=abc&state=tampered");
        request.Headers.Add("Cookie", $"mh_oidc_state={state}; mh_oidc_verifier={verifier}");

        var r = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Each_attempt_gets_a_fresh_state_and_verifier()
    {
        // Reusing either across attempts would defeat the point of both.
        var a = await Client().GetAsync("/auth/google/");
        var b = await Client().GetAsync("/auth/google/");

        Assert.NotEqual(CookieValue(a, "mh_oidc_state"), CookieValue(b, "mh_oidc_state"));
        Assert.NotEqual(CookieValue(a, "mh_oidc_verifier"), CookieValue(b, "mh_oidc_verifier"));
    }

    private static string? CookieValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(name, StringComparison.Ordinal))
                     ?.Split(';')[0].Split('=', 2)[1]
            : null;
}
