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

// Seed the first super admin.
//
// Not a migration: a migration cannot take configuration, and hard-coding an
// address into one would put a personal email in git forever and make every
// environment share the same first admin.
//
// Idempotent, so running it repeatedly is safe, and it never removes anyone —
// taking someone's access away is a deliberate act, not a side effect of a
// deploy.
var superAdmin = Environment.GetEnvironmentVariable("ARCTIC_SUPER_ADMIN_EMAIL");
if (string.IsNullOrWhiteSpace(superAdmin))
{
    Console.WriteLine(
        "No ARCTIC_SUPER_ADMIN_EMAIL set, so no super admin was seeded. " +
        "Without one, nobody can grant permissions to anyone.");
    return 0;
}

await using var seedSource = Npgsql.NpgsqlDataSource.Create(connectionString);

await using (var cmd = seedSource.CreateCommand("""
    INSERT INTO identity.people (kind, email)
    VALUES ('organizer', @email)
    ON CONFLICT DO NOTHING
    """))
{
    cmd.Parameters.AddWithValue("email", superAdmin);
    await cmd.ExecuteNonQueryAsync();
}

await using (var cmd = seedSource.CreateCommand("""
    INSERT INTO identity.team_members (person_id, team_id)
    SELECT p.id, t.id
      FROM identity.people p, identity.teams t
     WHERE lower(p.email) = lower(@email) AND t.slug = 'super-admin'
    ON CONFLICT DO NOTHING
    """))
{
    cmd.Parameters.AddWithValue("email", superAdmin);
    await cmd.ExecuteNonQueryAsync();
}

Console.WriteLine($"Super admin ensured for {superAdmin}.");

// The RBAC doc asks for two, so one graduation cannot lock the org out.
await using (var cmd = seedSource.CreateCommand("""
    SELECT count(*) FROM identity.team_members m
      JOIN identity.teams t ON t.id = m.team_id
     WHERE t.slug = 'super-admin' AND (m.expires_at IS NULL OR m.expires_at > now())
    """))
{
    if (await cmd.ExecuteScalarAsync() is long count && count < 2)
    {
        Console.WriteLine(
            $"Warning: {count} active super admin(s). The RBAC doc asks for two, " +
            "so that one graduation does not lock everyone out.");
    }
}

return 0;
