using Microsoft.Extensions.Caching.Memory;
using MorganHacks.Applications.Forms;
using MorganHacks.Observability;
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

        return app;
    }

    public sealed record MagicLinkRequest(string Email);

    /// <summary>
    /// Where the browser is, as opposed to where this service is.
    /// </summary>
    /// <remarks>
    /// The portal origin, never harbor's. Everything a person clicks has to
    /// stay on one hostname: the session cookie is host-only and SameSite=Lax,
    /// so a link that lands on the API's own origin sets a cookie the portal
    /// will never be sent.
    /// <para>
    /// The localhost default is for development, where the Next app runs on
    /// 3000 and proxies the API. In a deployed environment this is
    /// <c>PublicBaseUrl</c>, threaded through Bicep from the
    /// <c>PUBLIC_BASE_URL</c> environment variable exactly as
    /// <c>Google:RedirectUri</c> is — before that it silently defaulted here
    /// and every emailed link pointed at a machine nobody was running.
    /// </para>
    /// </remarks>
    private static string PublicBaseUrl(IConfiguration config) =>
        (config["PublicBaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

    /// <summary>
    /// Where the public forms site is, which is not where the portal is.
    /// </summary>
    /// <remarks>
    /// Its own origin, and that is the whole reason this exists rather than
    /// reusing <see cref="PublicBaseUrl"/>. The session cookie is host-only,
    /// so a link that lands on the portal sets a cookie the forms site will
    /// never be sent — somebody would click "sign in" in their inbox, arrive
    /// at a form, and be asked to sign in again by a browser that is holding a
    /// perfectly good session for a different hostname.
    /// <para>
    /// The localhost default is the port <c>deploy/local/dev.sh</c> starts
    /// portalforms on.
    /// </para>
    /// </remarks>
    public static string FormsBaseUrl(IConfiguration config) =>
        (config["FormsBaseUrl"] ?? "http://localhost:3002").TrimEnd('/');

    /// <summary>
    /// The path a form's sign-in link points at, on the forms site's origin.
    /// </summary>
    /// <remarks>
    /// Carries the <c>/api</c> prefix for the same reason
    /// <see cref="ConsumePath"/> does: that is the path portalforms proxies to
    /// harbor, and it is what puts the cookie on the origin the form is
    /// actually served from.
    /// </remarks>
    public static string FormLink(IConfiguration config, string code, string token) =>
        $"{FormsBaseUrl(config)}{ConsumePath}?token={token}&form={code}";

    /// <summary>
    /// The path the emailed link points at, on the portal's origin.
    /// </summary>
    /// <remarks>
    /// Carries the <c>/api</c> prefix because that is the path the portal
    /// proxies to harbor — atlas's own route is <c>/auth/consume</c>, and the
    /// browser's is <c>/api/auth/consume</c>. Without the prefix the link
    /// lands on a Next.js 404 and the account looks broken rather than the
    /// URL.
    /// </remarks>
    private const string ConsumePath = "/api/auth/consume";

    /// <summary>Where a consumed link puts them.</summary>
    private const string PortalPath = "/portal";

    /// <summary>
    /// Where a link that did not work puts them.
    /// </summary>
    /// <remarks>
    /// One destination for every rejection, matching the one message the
    /// endpoint used to return: expired, already used and never existed are
    /// not told apart, because telling them apart only helps somebody probing
    /// tokens. The query flag says a link failed, never which way.
    /// </remarks>
    private const string SignInPath = "/portal/sign-in?link=expired";

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

    /// <param name="scope">
    /// What the counter is counting. The forms site has its own sign-in step
    /// and shares this counter under a different scope, so attempts at a form
    /// do not consume somebody's attempts at the portal — they are separate
    /// places to be stuck, and being locked out of one for failing at the
    /// other is a support ticket nobody can diagnose.
    /// </param>
    public static bool TooManyFor(
        IMemoryCache cache, string email, string scope = "magic-link")
    {
        var key = $"{scope}:{email.Trim().ToLowerInvariant()}";

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
            await email.SendMagicLinkAsync(
                issued.PersonId, request.Email,
                $"{PublicBaseUrl(config)}{ConsumePath}?token={issued.Token}", ct);
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

    /// <summary>
    /// Spends a link and puts the person on their portal.
    /// </summary>
    /// <remarks>
    /// Redirects rather than answering JSON, because the only caller is a
    /// browser following a link out of an email. A person clicking "Sign in to
    /// MorganHacks" and landing on <c>{"signedIn":true}</c> has been shown the
    /// implementation and given nothing to do next.
    /// <para>
    /// Absolute, built from the same <see cref="PublicBaseUrl"/> the emailed
    /// link was, so the two cannot disagree about which host this environment
    /// is. A relative Location would work through the proxy and break for
    /// anyone reaching atlas directly.
    /// </para>
    /// </remarks>
    /// <param name="form">
    /// The form this link was issued from, when it was. Present only on links
    /// a sign-in form sent, and it changes nothing except where the browser
    /// lands: somebody who clicked "sign in" from an RSVP should arrive back
    /// at the RSVP, not on a portal they were not looking at.
    /// </param>
    private static async Task<IResult> ConsumeMagicLink(
        string? token,
        string? form,
        MagicLinkService links,
        SessionService sessions,
        HttpContext http,
        IConfiguration config,
        ILogger<MagicLinkRequest> log,
        CancellationToken ct)
    {
        var landing = Landing(config, form);

        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.Redirect(landing.Refused);
        }

        var result = await links.ConsumeAsync(token, ct);
        if (!result.Accepted)
        {
            // One destination for every rejection. Telling the caller whether
            // a token was expired, already used, or never existed only helps
            // somebody probing them.
            return Results.Redirect(landing.Refused);
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

        return Results.Redirect(landing.Accepted);
    }

    /// <summary>
    /// Where a consumed link puts somebody, and where a refused one does.
    /// </summary>
    /// <remarks>
    /// The form code is checked against its own shape before it is used, and
    /// that check is the only thing standing between this parameter and an
    /// open redirect. A code is seven characters from a fixed alphabet, so
    /// anything else — a slash, a scheme, a hostname, a second query string —
    /// is not a code and is treated as though no form was named at all. It is
    /// never escaped and reused; it either matches or it is dropped.
    /// <para>
    /// Both destinations are absolute and built from the same configured
    /// origin, so a link and the place it lands cannot disagree about which
    /// host this environment is.
    /// </para>
    /// </remarks>
    private static (string Accepted, string Refused) Landing(
        IConfiguration config, string? form)
    {
        if (form is not null && FormCode.Looks(form))
        {
            var forms = FormsBaseUrl(config);

            // The same query flag the portal's sign-in page uses. It says a
            // link did not work and never which way — expired, spent and never
            // issued are one answer, because telling them apart only helps
            // somebody probing tokens.
            return ($"{forms}/{form}", $"{forms}/{form}?link=expired");
        }

        var baseUrl = PublicBaseUrl(config);
        return ($"{baseUrl}{PortalPath}", $"{baseUrl}{SignInPath}");
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

    /// <summary>
    /// Who is signed in, and what they may do.
    /// </summary>
    /// <remarks>
    /// The permissions ride along because "what may I do" is a question about
    /// yourself, and answering it must not require permission to read anybody.
    /// The console used to work this out by fetching its own person record
    /// from <c>/admin/people/{id}</c>, which needs <c>people.view</c> — so
    /// somebody on the registration team, who holds <c>forms.manage</c> and
    /// not <c>people.view</c>, got an empty list back and had every button
    /// they were entitled to hidden from them.
    /// <para>
    /// Cosmetic either way. The gate refuses the request whether or not the
    /// button was drawn, and that refusal is the actual boundary — this only
    /// stops the console offering what it knows will be refused, and stops it
    /// hiding what will not be.
    /// </para>
    /// </remarks>
    private static async Task<IResult> WhoAmI(
        SessionService sessions,
        PermissionService permissions,
        HttpContext http,
        CancellationToken ct)
    {
        var token = http.Request.Cookies[SessionCookie];
        if (string.IsNullOrEmpty(token))
        {
            return Results.Unauthorized();
        }

        var result = await sessions.ValidateAsync(token, ct);
        if (!result.Accepted)
        {
            return Results.Unauthorized();
        }

        var effective = await permissions.ForAsync(result.PersonId, ct);

        return Results.Ok(new
        {
            personId = result.PersonId,
            permissions = effective.Granted.Select(p => p.Value).OrderBy(p => p),
        });
    }
}
