using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using MorganHacks.Api;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Data;
using MorganHacks.Applications.Services;
using MorganHacks.Applications.Storage;
using MorganHacks.Applications.Segments;
using MorganHacks.Audit;
using MorganHacks.Identity;
using MorganHacks.Identity.Services;
using MorganHacks.Api.Webhooks;
using MorganHacks.Observability;
using MorganHacks.Lark.Data.Data;
using Npgsql;
using MorganHacks.Features;

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

// The writing side of the same table. Separate from TemplateStore for the
// reason CampaignStore is separate from MessageQueue: that one is a single
// indexed read on the path of somebody signing in, and this one rewrites the
// rows every queued message points at.
builder.Services.AddSingleton<TemplateCatalog>();

// The broadcast side of the same schema. Separate from MessageQueue on
// purpose: one queues a single message on the path of somebody signing in,
// the other queues several hundred because an organizer decided to mail
// everybody, and they have opposite risks.
builder.Services.AddSingleton<CampaignStore>();

// Who a segment currently means. In Applications because it reads
// applications.*, and re-run on every preview and every send because the
// answer is different every day of registration week — which is why the
// resolved list is frozen into notify.messages rather than kept here.
builder.Services.AddSingleton<ISegmentResolver, PostgresSegmentResolver>();

// The public forms site reads one and writes the other. Both are stateless
// over the shared data source, so a singleton each.
builder.Services.AddSingleton<IFormStore, PostgresFormStore>();
builder.Services.AddSingleton<ISubmissionStore, PostgresSubmissionStore>();

// And the organizers' side reads back what the public side wrote. Separate
// from the submission store on purpose: they touch the same table and have
// opposite risks.
builder.Services.AddSingleton<IResponseStore, PostgresResponseStore>();

// The applicant's own side of a form that is not the application form: who is
// signed in, what they have already told us, and where their answer lands.
// Separate again, and for the sharpest version of the same reason — every
// query on it is scoped to one person, and nothing on it takes an id from a
// request.
builder.Services.AddSingleton<IRespondentStore, PostgresRespondentStore>();

builder.Services.AddScoped<IEmailSender, QueuedEmailSender>();

// Singletons, because both hold nothing but the data source — which is itself
// a singleton owning the connection pool. Scoping them would build a new
// wrapper per request around the same pool for no reason.
builder.Services.AddSingleton<IFormStore, PostgresFormStore>();
builder.Services.AddSingleton<IEventStore, PostgresEventStore>();

// The organizers' side of an application. Registered here rather than only in
// the portal's scope because the resume endpoint reads it.
builder.Services.AddSingleton<IApplicationStore, PostgresApplicationStore>();

// The door's read of the same table: one code in, one person out. Separate
// again, and for the plainest version of the reason -- it is reached with a
// permission that unlocks nothing else, by people holding a phone in a queue,
// and it must not grow a method that returns anything more than a name.
builder.Services.AddSingleton<ICheckInStore, PostgresCheckInStore>();

// And the registration team's read side of the same table, plus notes.
// Separate from the store above because that one owns the lifecycle: there is
// one way to change a status and this is deliberately not it.
builder.Services.AddSingleton<IApplicantStore, PostgresApplicantStore>();

// Resumes.
//
// Azure Blob rather than the R2 the plan suggests. We own the subscription
// this deploys into, so the container is declared in the same Bicep as
// everything else — no second account, no second set of credentials, and atlas
// reaches it with the managed identity it already holds to pull its own image.
// The portability the plan is protecting is that the database stores a key
// rather than a URL, and that lives in IResumeStore rather than in the choice
// of vendor.
//
// Registered whether or not it is configured, like Google sign-in above: with
// nothing set the upload endpoint answers 503 and the rest of the API works,
// rather than the service refusing to start on a laptop with no storage.
builder.Services.AddSingleton<IResumeStore>(_ => new AzureResumeStore(
    new ResumeStorageOptions
    {
        AccountName = builder.Configuration["Resumes:AccountName"],
        ConnectionString = builder.Configuration["Resumes:ConnectionString"],
        Container = builder.Configuration["Resumes:Container"] ?? "resumes",

        // Atlas holds a user-assigned identity. With more than one identity
        // available the credential chain picks the system-assigned one, which
        // has no role on the storage account, and every upload fails with an
        // authorization error that reads like a missing role assignment.
        ManagedIdentityClientId = builder.Configuration["Resumes:ClientId"],
    }));

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
// Proof that a request came through one of our front ends rather than from
// somebody reaching atlas or harbor directly. Only then are the forwarded
// address headers believed; empty means never, which is a worse rate limit
// rather than an absent one. See ClientAddress.
var proxySecret = builder.Configuration["Network:ProxySecret"];

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Generous, because SNS delivers bounce notifications in bulk after a
    // blast and throttling them means losing the record of who bounced —
    // which is the one thing this endpoint exists to capture.
    options.AddPolicy("webhook", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientAddress.ForRateLimit(http, proxySecret),
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

    // Uploading a resume. Same partition and the same generosity as the
    // submit, and for the same reason: the partition is an IP, and on campus
    // that is a whole building behind one NAT.
    //
    // What keeps this from being a way to fill a storage account is not the
    // request count — it is that every request is capped at five megabytes and
    // refused unless it is really a PDF. Tightening the count instead would
    // stop a launch meeting and would not stop a script.
    options.AddPolicy("resume-upload", http =>
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
        // The caller, not the front end that relayed them. See ClientAddress.
        var ip = ClientAddress.ForRateLimit(http, proxySecret);
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
        });
    });
});

// Feature flags. Loaded here so every flag is readable from the container that
// serves the request, and so a missing features.json stops the process rather
// than silently reading every flag as off.
builder.AddFeatures();

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



app.MapAuth();
app.MapEvents();
app.MapForms();
app.MapPortal();
app.MapCheckIn();
app.MapResumes();
app.MapPeopleAdmin();
app.MapAuditTrail();
app.MapFormsAdmin();
app.MapFormResponses();
app.MapApplicants();
app.MapTemplates();
app.MapCampaigns();
app.MapSesWebhook();

// Only here. Deployed environments are Staging or Production, set explicitly on
// every container, so this route does not exist there rather than existing and
// refusing — and a route that is absent cannot be reached by a misconfiguration.
if (app.Environment.IsDevelopment())
{
    app.MapDevSignIn();
}
app.MapGoogle();

app.Run();

/// <summary>Exposed so the test project can spin the API up in-process.</summary>
public partial class Program;
