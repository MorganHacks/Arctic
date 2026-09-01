using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using MorganHacks.Harbor;

var builder = WebApplication.CreateBuilder(args);

// Behind Cloudflare and an ingress, every request arrives from a proxy, so
// RemoteIpAddress is that proxy rather than the caller. Left uncorrected the
// per-IP limiters collapse into one bucket shared by the entire internet —
// ten sign-ins per quarter hour for everybody — and sessions.ip records our
// own edge, which makes the audit column worthless.
//
// Trust is opt-in and explicit. Clearing KnownProxies unconditionally is the
// usual version of this fix and it is worse than the bug: it lets any caller
// set X-Forwarded-For and walk straight past the limiter. With nothing
// configured this trusts nothing and behaves exactly as it does today.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // One hop by default. Raise it to the number of proxies actually in front
    // of this service and no higher: each extra hop is one more entry of
    // X-Forwarded-For that a caller is allowed to have written themselves.
    options.ForwardLimit = builder.Configuration.GetValue<int?>("Network:ForwardLimit") ?? 1;

    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();

    foreach (var proxy in builder.Configuration
                 .GetSection("Network:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }

    foreach (var cidr in builder.Configuration
                 .GetSection("Network:KnownNetworks").Get<string[]>() ?? [])
    {
        var parts = cidr.Split('/');
        if (parts.Length == 2
            && IPAddress.TryParse(parts[0], out var prefix)
            && int.TryParse(parts[1], out var length))
        {
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, length));
        }
    }
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Two layers, doing different jobs. Cloudflare absorbs volume — the botnet,
// the scraper, the traffic that should never reach the cluster. Harbor handles
// per-identity limits Cloudflare cannot express, because it does not know this
// is the fourth magic-link request for one address in a minute when those
// requests came from four different addresses.
//
// In-memory and per pod, so with two replicas the real limit is roughly
// double. Accept the imprecision: a shared counter means Redis, and Redis
// means another thing to run, pay for, and have fall over at 2am during
// registration week. Set it at half what you want.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth-strict", Limiter(permits: 10, perMinutes: 15));
    options.AddPolicy("standard", Limiter(permits: 300, perMinutes: 1));
});

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
              ?? ["https://morganhacks.com", "https://www.morganhacks.com"];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    // Never AllowAnyOrigin: sessions are cookies, and a wildcard origin with
    // credentials is both forbidden by the spec and a bad idea on its own.
    .WithOrigins(origins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithExposedHeaders(IdentityHeaders.CorrelationId)));

var app = builder.Build();

// Order is not arbitrary.
//
//   0. forwarded headers   resolve the real client IP before anyone reads it
//   1. correlation id      first thing to see the request
//   2. rate limiting       cheap, and must reject before any database work
//   3. identity            strip caller-supplied identity, attach our own
//   4. forward             YARP proxies to atlas
//
// Rate limiting sits ahead of identity on purpose: identity costs a lookup,
// and someone hammering us should be rejected before they cost us one each.
// Ahead of everything: the limiter partitions on the caller's IP, and
// without this that IP is Cloudflare's for every request on the internet.
app.UseForwardedHeaders();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseCors();
app.UseRateLimiter();
app.UseMiddleware<IdentityMiddleware>();

// Liveness. Static 200, and deliberately no database or upstream check: a
// Postgres blip that restarts every harbor pod turns a recoverable problem
// into a full outage, and harbor is the only path to the API.
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapReverseProxy();

app.Run();

static Func<HttpContext, RateLimitPartition<string>> Limiter(int permits, int perMinutes) =>
    http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(perMinutes),
            QueueLimit = 0,
        });

/// <summary>Exposed so tests can host harbor in-process.</summary>
public partial class Program;
