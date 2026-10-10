using Npgsql;

namespace MorganHacks.Seed;

public static class SeedSafety
{
    public static void CheckEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        foreach (var key in new[] { "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT" })
        {
            var value = read(key);
            if (!string.IsNullOrEmpty(value) && value != "Development")
                throw new InvalidOperationException($"Refusing seed: {key} must be unset or Development.");
        }
        var target = read("ARCTIC_TARGET");
        if (!string.IsNullOrEmpty(target) && target != "local")
            throw new InvalidOperationException("Refusing seed: ARCTIC_TARGET must be local.");
        if (!string.IsNullOrEmpty(read("ARCTIC_DB")))
            throw new InvalidOperationException("Unset ARCTIC_DB before seeding. Only the verified local Compose database is supported.");
    }
    public static string StagingConnection(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        if (read("ARCTIC_TARGET") != "staging" || read("DOTNET_ENVIRONMENT") != "Staging"
            || read("ASPNETCORE_ENVIRONMENT") is { Length: > 0 } webEnvironment && webEnvironment != "Staging")
            throw new InvalidOperationException("Staging seeding requires ARCTIC_TARGET=staging and DOTNET_ENVIRONMENT=Staging.");
        NpgsqlConnectionStringBuilder connection;
        try { connection = new(read("ARCTIC_DB") ?? ""); }
        catch (ArgumentException) { throw new InvalidOperationException("Invalid staging database configuration."); }
        if (connection.Host != "psql-mh-staging.postgres.database.azure.com"
            || connection.Port != 5432 || connection.Database != "morganhacks"
            || connection.Username != "arctic" || connection.SslMode != SslMode.VerifyFull
            || string.IsNullOrEmpty(connection.Password))
            throw new InvalidOperationException("Refusing seed: only the verified-TLS MorganHacks staging database is allowed.");
        return connection.ConnectionString;
    }
}
