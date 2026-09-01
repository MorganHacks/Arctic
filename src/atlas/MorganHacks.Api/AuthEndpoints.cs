using Microsoft.Extensions.Caching.Memory;
using MorganHacks.Observability;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api;

public static class AuthEndpoints
{
    /// <summary>
    /// The session cookie.
    /// </summary>
    /// <remarks>
    /// HttpOnly so an XSS bug cannot lift the session. Secure so it never
    /// crosses plain HTTP. SameSite=Lax so it survives the click from an email
    /// client — Strict would drop the cookie on exactly the navigation a magic
    /// link produces, and the user would land logged out.
    /// </remarks>
    private const string SessionCookie = RequirePermissionExtensions.SessionCookie;

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth");

        // Only this one is rate limited. Throttling /me would break the app
        // for a legitimately busy session.
        auth.MapPost("/magic-link", RequestMagicLink).RequireRateLimiting("magic-link");
        auth.MapGet("/consume", ConsumeMagicLink);
        auth.MapPost("/logout", Logout);
        auth.MapGet("/me", WhoAmI);

        // The first gated route. Proves the filter end to end and gives the
        // admin screens something to call.
        app.MapGet("/people", ListPeople)
           .RequirePermission(Permission.PeopleView);

        return app;
    }

    public sealed record MagicLinkRequest(string Email);

    /// <summary>Requires <c>people.view</c>.</summary>
    private static IResult ListPeople(HttpContext http) =>
        Results.Ok(new { requestedBy = http.PersonId(), people = Array.Empty<object>() });

    /// <summary>
    /// Per-address request counter, checked before any database work.
    /// </summary>
    /// <remarks>
    /// The middleware limits per IP; this limits per address. Both are needed
    /// because either alone is trivially bypassed — one address from many
    /// hosts, or many addresses from one host.
    /// <para>
    /// In memory, so with several replicas the real limit is roughly this
    /// times the replica count. That imprecision is accepted deliberately: a
    /// shared counter means Redis, and Redis means another thing to run, pay
    /// for, and have fall over at 2am during registration week.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan AddressWindow = TimeSpan.FromMinutes(15);
    private const int MaxPerAddress = 3;

    private sealed class AddressCounter
    {
        public int Count;
    }

    private static bool TooManyFor(IMemoryCache cache, string email)
    {
        var key = $"magic-link:{email.Trim().ToLowerInvariant()}";

        // A cache rather than a dictionary, because entries have to expire on
        // their own. Keying a plain dictionary by every address ever
        // submitted only ever grows it, and this endpoint is open to the
        // internet, so that is a memory leak anyone can drive.
        var counter = cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = AddressWindow;
            return new AddressCounter();
        })!;

        lock (counter)
        {
            if (counter.Count >= MaxPerAddress)
            {
                return true;
            }

            counter.Count++;
            return false;
        }
    }

    /// <summary>
    /// Always answers the same way, whether or not the address exists.
    /// </summary>
    /// <remarks>
    /// This is the whole point of the endpoint. "No account found" for one
    /// address and "check your inbox" for another turns it into a lookup
    /// service for who applied to the hackathon.
    /// <para>
    /// The response is identical in status and body. Timing is close but not
    /// constant: a known address costs one extra INSERT. Closing that gap
    /// properly means queueing the send, which is lark's job — until then the
    /// rate limiter is what makes the difference impractical to measure.
    /// </para>
    /// </remarks>
    private static async Task<IResult> RequestMagicLink(
        MagicLinkRequest request,
        MagicLinkService links,
        IEmailSender email,
        IConfiguration config,
        IMemoryCache cache,
        ILogger<MagicLinkRequest> log,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest(new { error = "An email address is required." });
        }

        // Before any database work, as the auth doc requires.
        if (TooManyFor(cache, request.Email))
        {
            // Same body as success. Saying "too many requests for this address"
            // would confirm the address exists, which is what the identical
            // response is there to hide.
            return Results.Accepted(value: new
            {
                message = "If that address has an account, a sign-in link is on its way.",
            });
        }

        var issued = await links.IssueAsync(request.Email, ct);

        if (issued is not null)
        {
            var baseUrl = config["PublicBaseUrl"] ?? "http://localhost:3000";
            await email.SendMagicLinkAsync(
                issued.PersonId, request.Email,
                $"{baseUrl}/auth/consume?token={issued.Token}", ct);
        }
        else
        {
            // Logged without the address: knowing someone probed an unknown
            // address is useful, knowing which address is not worth storing.
            log.LogInformation("Magic link requested for an unknown address.");
        }

        return Results.Accepted(value: new
        {
            message = "If that address has an account, a sign-in link is on its way.",
        });
    }

    private static async Task<IResult> ConsumeMagicLink(
        string? token,
        MagicLinkService links,
        SessionService sessions,
        HttpContext http,
        ILogger<MagicLinkRequest> log,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.BadRequest(new { error = "Missing token." });
        }

        var result = await links.ConsumeAsync(token, ct);
        if (!result.Accepted)
        {
            // One message for every rejection. Telling the caller whether a
            // token was expired, already used, or never existed only helps
            // somebody probing them.
            return Results.BadRequest(new
            {
                error = "That sign-in link is no longer valid. Request a new one.",
            });
        }

        var sessionToken = await sessions.StartAsync(
            result.PersonId,
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

        // The other half of the pair. Requested staying healthy while this
        // collapses is the signal that mail is not being delivered, and it is
        // the failure no error rate catches because nothing is erroring.
        log.LogInformation(
            "Signed in from a link. {event}", Events.MagicLinkConsumed);

        return Results.Ok(new { signedIn = true });
    }

    private static async Task<IResult> Logout(
        SessionService sessions, HttpContext http, CancellationToken ct)
    {
        var token = http.Request.Cookies[SessionCookie];
        if (!string.IsNullOrEmpty(token))
        {
            await sessions.RevokeAsync(token, ct);
        }

        http.Response.Cookies.Delete(SessionCookie);
        return Results.Ok(new { signedOut = true });
    }

    private static async Task<IResult> WhoAmI(
        SessionService sessions, HttpContext http, CancellationToken ct)
    {
        var token = http.Request.Cookies[SessionCookie];
        if (string.IsNullOrEmpty(token))
        {
            return Results.Unauthorized();
        }

        var result = await sessions.ValidateAsync(token, ct);
        return result.Accepted
            ? Results.Ok(new { personId = result.PersonId })
            : Results.Unauthorized();
    }
}
