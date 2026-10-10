using MorganHacks.Seed;
using Npgsql;

try
{
    var count = 50;
    var apply = false;
    var staging = false;
    string? systemId = null;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--staging": staging = true; break;
            case "--apply": apply = true; break;
            case "--count" when i + 1 < args.Length && int.TryParse(args[++i], out var n): count = n; break;
            case "--local-system-id" when i + 1 < args.Length: systemId = args[++i]; break;
            default: throw new InvalidOperationException("Usage: deploy/local/seed-hackers.sh [--count 50] [--apply]");
        }
    }
    string connectionString;
    if (staging)
    {
        if (systemId is not null) throw new InvalidOperationException("Do not combine staging and local verification.");
        connectionString = SeedSafety.StagingConnection();
    }
    else
    {
        SeedSafety.CheckEnvironment();
        connectionString = "Host=127.0.0.1;Port=5432;Database=morganhacks;Username=arctic;Password=local-dev-only;Timeout=5;Pooling=false";
    }
    if (count is < 1 or > 1000) throw new InvalidOperationException("Count must be between 1 and 1000.");
    Console.WriteLine($"{(staging ? "Staging" : "Local")} organizer simulation: {count} applicants in '{SeedRunner.EventName}'.");
    if (!apply)
    {
        Console.WriteLine("Preview only; no database connection or writes. Add --apply to create missing applicants.");
        return 0;
    }
    if (!staging && string.IsNullOrWhiteSpace(systemId))
        throw new InvalidOperationException("Use deploy/local/seed-hackers.sh to verify the local Docker database first.");

    await using var source = NpgsqlDataSource.Create(connectionString);
    if (!staging)
    {
        await using var check = source.CreateCommand("SELECT system_identifier::text FROM pg_control_system()");
        if ((string?)await check.ExecuteScalarAsync() != systemId)
            throw new InvalidOperationException("The database does not match the verified local Docker container. Refusing writes.");
    }
    var result = await new SeedRunner(source).RunAsync(count);
    Console.WriteLine($"Created {result.Created}; kept {result.Skipped} existing applicants unchanged.");
    var console = staging ? "https://admin-stg.morganhacks.com" : "http://localhost:3001";
    Console.WriteLine($"Open {console}/applicants?event={SeedRunner.EventId}");
    Console.WriteLine("No mail queued. Hacker login and resume fixtures are outside this organizer-only seed.");
    return 0;
}
catch (Exception e)
{
    // Avoid dumping connection strings or application answers in diagnostics.
    Console.Error.WriteLine(e is NpgsqlException
        ? "Database operation failed. Ensure the target database is reachable and migrations have been applied."
        : e.Message);
    return 1;
}
