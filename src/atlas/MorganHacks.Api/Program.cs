var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Liveness only. This deliberately does NOT touch the database: a Postgres
// blip that restarts every pod turns a recoverable problem into an outage.
// Readiness, which may check dependencies, is a separate endpoint added when
// there are dependencies to check.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Exposed so the test project can spin the API up in-process.</summary>
public partial class Program;
