using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using MorganHacks.Api;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Data;
using MorganHacks.Applications.Services;
using MorganHacks.Audit;
using MorganHacks.Identity;
using MorganHacks.Identity.Services;
using MorganHacks.Api.Webhooks;
using MorganHacks.Observability;
using MorganHacks.Lark.Data.Data;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.UseArcticLogging("atlas");

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
// Magic links are queued, not sent here. Sending inline would put SES's
// availability in the path of somebody clicking "sign in", and would skip the
// suppression list that stops us mailing an address that already bounced.
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
// Only the read side. The trail is written by triggers whether or not this
// line is here, which is the point — recording a permission change is not
// something a service can be configured out of.
builder.Services.AddAuditTrail();
builder.Services.AddSingleton<TemplateStore>();
builder.Services.AddSingleton<MessageQueue>();

// The public forms site reads one and writes the other. Both are stateless
// over the shared data source, so a singleton each.
builder.Services.AddSingleton<IFormStore, PostgresFormStore>();
builder.Services.AddSingleton<ISubmissionStore, PostgresSubmissionStore>();

builder.Services.AddScoped<IEmailSender, QueuedEmailSender>();

// Singletons, because both hold nothing but the data source — which is itself
// a singleton owning the connection pool. Scoping them would build a new
// wrapper per request around the same pool for no reason.
builder.Services.AddSingleton<IFormStore, PostgresFormStore>();
builder.Services.AddSingleton<IEventStore, PostgresEventStore>();

// Enums cross the wire as names, matching how PostgresFormStore writes them
// to the column. A form field's type would otherwise be a 6 in the JSON and a
// "consent" in the database, so the builder would be reading and writing
// numbers that mean nothing on screen and change meaning if the enum is ever
// reordered.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// The applicant's own view of their application. Registered separately from
// the organizers' store because it is a different surface with the opposite
// default: every query on it is scoped to one person.
builder.Services.AddScoped<IApplicantPortalStore, PostgresApplicantPortalStore>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<ISnsSignatureVerifier, SnsSignatureVerifier>();
builder.Services.AddMemoryCache();

// So a queued message can be stamped with the request that caused it.
builder.Services.AddHttpContextAccessor();

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

    // Generous, because SNS delivers bounce notifications in bulk after a
    // blast and throttling them means losing the record of who bounced —
    // which is the one thing this endpoint exists to capture.
    options.AddPolicy("webhook", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // Submitting an application. Deliberately loose, and the looseness is the
    // decision worth explaining.
    //
    // A real applicant submits once — the unique index on (event_id,
    // lower(email)) is what actually stops one person applying repeatedly, and
    // it holds no matter how many requests they make. So this limiter is not
    // there to stop an individual; it is there so a script cannot hold the
    // endpoint open all night inventing addresses.
    //
    // Tightening it hurts the wrong people. The partition is an IP, and on
    // campus that is an entire building behind one NAT: a launch meeting where
    // sixty people submit in the same five minutes is the exact traffic this
    // must not refuse.
    options.AddPolicy("form-submit", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            }));

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

// Ahead of anything that logs, so no line this request produces is missing
// the one field that ties it to the rest of its journey.
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseRateLimiter();

// Liveness only. This deliberately does NOT touch the database: a Postgres
// blip that restarts every pod turns a recoverable problem into an outage.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));



// TEMPORARY. Reports what this hop sees, so the client-IP chain through
// Vercel can be read rather than reasoned about. Removed once measured.
app.MapGet("/auth/whatismyip", (HttpContext http) => Results.Ok(new
{
    remoteIp = http.Connection.RemoteIpAddress?.ToString(),
    forwardedFor = http.Request.Headers["X-Forwarded-For"].ToString(),
    originalFor = http.Request.Headers["X-Original-For"].ToString(),
    vercelFor = http.Request.Headers["X-Vercel-Forwarded-For"].ToString(),
    realIp = http.Request.Headers["X-Real-IP"].ToString(),
}));

app.MapAuth();
app.MapForms();
app.MapPortal();
app.MapPeopleAdmin();
app.MapAuditTrail();
app.MapFormsAdmin();
app.MapSesWebhook();
app.MapGoogle();

app.Run();

/// <summary>Exposed so the test project can spin the API up in-process.</summary>
public partial class Program;
