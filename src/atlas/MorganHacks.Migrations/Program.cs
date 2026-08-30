using System.Reflection;
using DbUp;
using DbUp.Engine;

// The schema owner. Runs as a pre-deploy job and is the only thing in the
// system that changes the database structure.
//
//   dotnet run                 apply anything outstanding
//   dotnet run -- --whatif     list what would run, change nothing

var whatIf = args.Contains("--whatif");

var connectionString =
    Environment.GetEnvironmentVariable("ARCTIC_DB")
    ?? "Host=localhost;Port=5432;Database=morganhacks;Username=arctic;Password=local-dev-only";

// Never print the connection string: it carries the password.
var host = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
Console.WriteLine($"Target: {host.Host}:{host.Port}/{host.Database} as {host.Username}");

var upgrader = DeployChanges.To
    .PostgresqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
    // Every script runs inside a transaction, so a failure half way through
    // leaves the schema as it was rather than partially migrated.
    .WithTransactionPerScript()
    .LogToConsole()
    .Build();

if (whatIf)
{
    var pending = upgrader.GetScriptsToExecute();
    if (pending.Count == 0)
    {
        Console.WriteLine("Up to date. Nothing to apply.");
        return 0;
    }

    Console.WriteLine($"{pending.Count} script(s) would run:");
    foreach (var script in pending)
    {
        Console.WriteLine($"  {script.Name}");
    }

    return 0;
}

DatabaseUpgradeResult result = upgrader.PerformUpgrade();

if (!result.Successful)
{
    Console.Error.WriteLine($"Migration failed: {result.Error}");
    return 1;
}

Console.WriteLine("Schema is up to date.");
return 0;
