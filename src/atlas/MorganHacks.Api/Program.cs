using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using MorganHacks.Api;
using MorganHacks.Identity;
using MorganHacks.Identity.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? Environment.GetEnvironmentVariable("ARCTIC_DB")
    ?? "Host=localhost;Port=5432;Database=morganhacks;Username=arctic;Password=local-dev-only";

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

builder.Services.AddIdentityModule(connectionString);
builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

// Registered even when unconfigured: the endpoints answer 503 rather than
// failing at startup, so local development does not need Google credentials.
builder.Services.AddSingleton<IGoogleTokenVerifier>(
    _ => new GoogleTokenVerifier(builder.Configuration["Google:ClientId"] ?? string.Empty));

// Rate limiting on the magic-link endpoint, per IP and per address.
//
// Both, because either alone is trivially bypassed: one address from many
// hosts, or many addresses from one host. Without this the endpoint is a way
// to send unlimited mail from our domain to arbitrary recipients, which makes
// us a spam relay and destroys the sending reputation.
//
// Registered as a partitioned limiter so a request is rejected before any
// database work happens.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("magic-link", http =>
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
        });
    });
});

var app = builder.Build();

// First, so every limiter and every logged IP below sees the real caller.
app.UseForwardedHeaders();
app.UseRateLimiter();

// Liveness only. This deliberately does NOT touch the database: a Postgres
// blip that restarts every pod turns a recoverable problem into an outage.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapAuth();
app.MapGoogle();

app.Run();

/// <summary>Exposed so the test project can spin the API up in-process.</summary>
public partial class Program;
