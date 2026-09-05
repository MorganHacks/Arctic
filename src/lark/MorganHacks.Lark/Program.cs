using Amazon.SimpleEmailV2;
using MorganHacks.Lark;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Sending;
using MorganHacks.Observability;
using Npgsql;
using MorganHacks.Features;

var builder = Host.CreateApplicationBuilder(args);

builder.UseArcticLogging("lark");

var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? Environment.GetEnvironmentVariable("ARCTIC_DB")
    ?? "Host=localhost;Port=5432;Database=morganhacks;Username=arctic;Password=local-dev-only";

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<MessageQueue>();
builder.Services.AddSingleton<TemplateStore>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<SendLoopOptions>(builder.Configuration.GetSection("SendLoop"));

// Region and credentials come from the environment, so nothing secret is in
// the repo and the same binary runs locally, on staging and in production.
//
// Constructing the SES client without a region throws, so an unset variable
// would take the whole worker down at startup and keep it down — a crash loop
// that reports a dependency-injection stack trace rather than "no region
// configured". Checked here instead, and the worker runs either way.
var awsRegion = builder.Configuration["AWS_REGION"]
                ?? Environment.GetEnvironmentVariable("AWS_REGION");

if (!string.IsNullOrWhiteSpace(awsRegion))
{
    builder.Services.AddSingleton<IAmazonSimpleEmailServiceV2>(
        _ => new AmazonSimpleEmailServiceV2Client(
            Amazon.RegionEndpoint.GetBySystemName(awsRegion)));
    builder.Services.AddSingleton<IEmailProvider, SesEmailProvider>();
}
else
{
    builder.Services.AddSingleton<IEmailProvider, UnconfiguredEmailProvider>();
}

builder.Services.AddHostedService<SendLoop>();

// Feature flags. None are read here yet; the call is what makes adding the
// first one a one-line change, and what makes a missing features.json fail
// at start-up rather than at the moment somebody first relies on a flag.
builder.AddFeatures();

var host = builder.Build();
host.Run();
