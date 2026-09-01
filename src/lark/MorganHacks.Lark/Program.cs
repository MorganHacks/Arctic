using Amazon.SimpleEmailV2;
using MorganHacks.Lark;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Sending;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddSingleton<IAmazonSimpleEmailServiceV2>(
    _ => new AmazonSimpleEmailServiceV2Client());
builder.Services.AddSingleton<IEmailProvider, SesEmailProvider>();

builder.Services.AddHostedService<SendLoop>();

var host = builder.Build();
host.Run();
