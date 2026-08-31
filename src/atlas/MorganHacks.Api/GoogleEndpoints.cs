using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api;

/// <summary>
/// Organizer sign-in through Google, using the authorization code flow with
/// PKCE.
/// </summary>
public static class GoogleEndpoints
{
    private const string StateCookie = "mh_oidc_state";
    private const string VerifierCookie = "mh_oidc_verifier";
    private const string SessionCookie = RequirePermissionExtensions.SessionCookie;

    public static IEndpointRouteBuilder MapGoogle(this IEndpointRouteBuilder app)
    {
        var google = app.MapGroup("/auth/google");
        google.MapGet("/", Start);
        google.MapGet("/callback", Callback);
        return app;
    }

    private static IResult Start(HttpContext http, IConfiguration config)
    {
        var clientId = config["Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Results.Problem("Google sign-in is not configured.", statusCode: 503);
        }

        // PKCE. The verifier never leaves us; only its hash goes to Google, so
        // an intercepted authorization code is useless without it.
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        // Binds the callback to this browser, so a code delivered to someone
        // else's session cannot be redeemed.
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var options = TransientCookie();
        http.Response.Cookies.Append(StateCookie, state, options);
        http.Response.Cookies.Append(VerifierCookie, verifier, options);

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = RedirectUri(config),
            ["response_type"] = "code",
            ["scope"] = "openid email",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            // Google returns a refresh token we neither want nor store: this
            // is authentication, not access to anybody's Google data.
            ["access_type"] = "online",
            ["prompt"] = "select_account",
        };

        return Results.Redirect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            "https://accounts.google.com/o/oauth2/v2/auth", query));
    }

    private static async Task<IResult> Callback(
        string? code,
        string? state,
        HttpContext http,
        IConfiguration config,
        IGoogleTokenVerifier verifier,
        IIdentityStore store,
        SessionService sessions,
        IHttpClientFactory clients,
        ILogger<GoogleIdentity> log,
        CancellationToken ct)
    {
        var expectedState = http.Request.Cookies[StateCookie];
        var codeVerifier = http.Request.Cookies[VerifierCookie];

        http.Response.Cookies.Delete(StateCookie);
        http.Response.Cookies.Delete(VerifierCookie);

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state)
            || string.IsNullOrEmpty(expectedState) || string.IsNullOrEmpty(codeVerifier))
        {
            return Results.BadRequest(new { error = "Sign-in could not be completed." });
        }

        // Fixed-time comparison: a leaky compare here would let state be
        // guessed a character at a time.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState)))
        {
            return Results.BadRequest(new { error = "Sign-in could not be completed." });
        }

        var clientId = config["Google:ClientId"];
        var clientSecret = config["Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return Results.Problem("Google sign-in is not configured.", statusCode: 503);
        }

        using var client = clients.CreateClient();
        using var response = await client.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = RedirectUri(config),
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = codeVerifier,
            }), ct);

        if (!response.IsSuccessStatusCode)
        {
            log.LogWarning("Google token exchange failed with {Status}.", response.StatusCode);
            return Results.BadRequest(new { error = "Sign-in could not be completed." });
        }

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!payload.RootElement.TryGetProperty("id_token", out var idTokenElement))
        {
            return Results.BadRequest(new { error = "Sign-in could not be completed." });
        }

        var identity = await verifier.VerifyAsync(idTokenElement.GetString()!, ct);
        if (identity is null)
        {
            return Results.BadRequest(new { error = "Sign-in could not be completed." });
        }

        // Google has said who they are. Whether they are allowed in is our
        // question, answered against our own allowlist.
        var organizer = await store.ResolveOrganizerAsync(identity, ct);
        if (!organizer.Accepted)
        {
            // Logged with the reason but not the address: useful for support,
            // not worth storing PII for.
            log.LogInformation("Organizer sign-in refused: {Reason}", organizer.Rejection);
            return Results.Json(
                new { error = "That account is not set up as an organizer." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var sessionToken = await sessions.StartAsync(
            organizer.PersonId,
            http.Request.Headers.UserAgent.ToString(),
            http.Connection.RemoteIpAddress?.ToString(),
            ct);

        http.Response.Cookies.Append(SessionCookie, sessionToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = SessionService.Lifetime,
            Path = "/",
        });

        return Results.Ok(new { signedIn = true });
    }

    /// <summary>
    /// Short-lived, and Lax rather than Strict: the callback is a top-level
    /// navigation from Google, and Strict would drop these on exactly that hop.
    /// </summary>
    private static CookieOptions TransientCookie() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        MaxAge = TimeSpan.FromMinutes(10),
        Path = "/auth/google",
    };

    private static string RedirectUri(IConfiguration config) =>
        config["Google:RedirectUri"] ?? "http://localhost:5080/auth/google/callback";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
