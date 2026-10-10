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
}
