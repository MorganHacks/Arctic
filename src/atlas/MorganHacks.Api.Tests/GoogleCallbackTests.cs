using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Hosting;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>Returns whatever identity a test says Google returned.</summary>
internal sealed class StubVerifier(GoogleIdentity? identity) : IGoogleTokenVerifier
{
    public Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct = default) =>
        Task.FromResult(identity);
}

/// <summary>Stands in for Google's token endpoint.</summary>
internal sealed class StubTokenEndpoint : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id_token = "a-token-the-verifier-decides-about" }),
        });
}

/// <summary>
/// What the browser is told once the callback finishes.
/// </summary>
/// <remarks>
/// The callback is a top-level navigation — somebody clicked a Google button
/// and is watching the address bar. Answering it with a JSON body leaves them
/// looking at raw output with nowhere to go, so where it sends them is part of
/// the behaviour rather than a detail.
/// <para>
/// Google's half is stubbed. Verifying a real token is Google's job and is
/// tested by trusting their library; what belongs to us is what happens after.
/// </para>
/// </remarks>
public class GoogleCallbackTests(IdentityDatabase db)
    : IClassFixture<IdentityDatabase>, IAsyncLifetime
{
    private static string Unique(string p) => $"{p}-{Guid.NewGuid():N}@example.com";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    private WebApplicationFactory<Program> AppReturning(GoogleIdentity? identity) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            b.UseSetting("Google:ClientId", "test-client-id.apps.googleusercontent.com");
            b.UseSetting("Google:ClientSecret", "test-secret");
            b.UseSetting("Google:RedirectUri", "https://console.test/api/auth/google/callback");

            b.ConfigureServices(services =>
            {
                services.RemoveAll<IGoogleTokenVerifier>();
                services.AddSingleton<IGoogleTokenVerifier>(new StubVerifier(identity));
                services.ConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(
                        builder => builder.PrimaryHandler = new StubTokenEndpoint()));
            });
        });

    private static string? Cookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(name, StringComparison.Ordinal))
                ?.Split(';')[0].Split('=', 2)[1]
            : null;

    /// <summary>Drives start then callback, carrying the state and verifier across.</summary>
    private static async Task<HttpResponseMessage> SignInAsync(
        WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var start = await client.GetAsync("/auth/google/");
        var state = Cookie(start, "mh_oidc_state")!;
        var verifier = Cookie(start, "mh_oidc_verifier")!;

        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/auth/google/callback?code=a-code&state={state}");
        request.Headers.Add("Cookie", $"mh_oidc_state={state}; mh_oidc_verifier={verifier}");

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task An_allowlisted_organizer_lands_back_on_the_console()
    {
        var email = Unique("organizer");
        await db.AddPersonAsync(email, "organizer");
        using var app = AppReturning(new GoogleIdentity($"sub-{Guid.NewGuid():N}", email));

        var response = await SignInAsync(app);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_successful_sign_in_sets_the_session_cookie()
    {
        var email = Unique("organizer");
        await db.AddPersonAsync(email, "organizer");
        using var app = AppReturning(new GoogleIdentity($"sub-{Guid.NewGuid():N}", email));

        var response = await SignInAsync(app);

        var session = Cookie(response, "mh_session");
        Assert.False(string.IsNullOrEmpty(session));
    }

    [Fact]
    public async Task Somebody_who_is_not_an_organizer_is_sent_back_to_sign_in()
    {
        // Google authenticated them perfectly well. They are still not an
        // organizer, and the page they land on has to be one they can act on.
        using var app = AppReturning(
            new GoogleIdentity($"sub-{Guid.NewGuid():N}", Unique("stranger")));

        var response = await SignInAsync(app);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/sign-in?error=1", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_refusal_sets_no_session()
    {
        using var app = AppReturning(
            new GoogleIdentity($"sub-{Guid.NewGuid():N}", Unique("stranger")));

        var response = await SignInAsync(app);

        Assert.Null(Cookie(response, "mh_session"));
    }

    [Fact]
    public async Task Every_redirect_out_of_the_callback_is_a_relative_path()
    {
        // Neither destination comes from configuration or from the request.
        // A destination taken from either and followed unchecked is how an
        // open redirect gets built, and a sign-in page is exactly where one
        // would be worth the most to somebody phishing organizers.
        foreach (var identity in new GoogleIdentity?[]
                 {
                     new($"sub-{Guid.NewGuid():N}", Unique("stranger")),
                     null,
                 })
        {
            using var app = AppReturning(identity);
            var response = await SignInAsync(app);

            if (response.Headers.Location is { } location)
            {
                Assert.False(location.IsAbsoluteUri, $"{location} left this origin");
            }
        }
    }
}
