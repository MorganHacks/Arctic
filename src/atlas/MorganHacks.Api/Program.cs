using System.Threading.RateLimiting;
using MorganHacks.Api;
using MorganHacks.Identity;
using MorganHacks.Identity.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? Environment.GetEnvironmentVariable("ARCTIC_DB")
    ?? "Host=localhost;Port=5432;Database=morganhacks;Username=arctic;Password=local-dev-only";

builder.Services.AddIdentityModule(connectionString);
builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
builder.Services.AddHttpClient();

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

app.UseRateLimiter();

// Liveness only. This deliberately does NOT touch the database: a Postgres
// blip that restarts every pod turns a recoverable problem into an outage.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapAuth();
app.MapGoogle();

app.Run();

/// <summary>Exposed so the test project can spin the API up in-process.</summary>
public partial class Program;
