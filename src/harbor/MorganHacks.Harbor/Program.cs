using System.Threading.RateLimiting;
using MorganHacks.Harbor;

var builder = WebApplication.CreateBuilder(args);

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
//   1. correlation id      first thing to see the request
//   2. rate limiting       cheap, and must reject before any database work
//   3. identity            strip caller-supplied identity, attach our own
//   4. forward             YARP proxies to atlas
//
// Rate limiting sits ahead of identity on purpose: identity costs a lookup,
// and someone hammering us should be rejected before they cost us one each.
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
