using System.Reflection;
using DbUp;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MorganHacks.Api.Tests;

/// <summary>
/// A throwaway Postgres for the tests that genuinely need one.
/// </summary>
/// <remarks>
/// The schema is applied by running the real migrations rather than by a
/// hand-written setup script, so these tests fail if a migration breaks —
/// which is the point of having them.
/// </remarks>
public sealed class IdentityDatabase : IAsyncLifetime
{
    // Same major version as docker-compose, so a behaviour difference between
    // local and CI is one less thing to chase.
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:18-alpine").Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    /// <summary>Exposed so the API can be pointed at this same database.</summary>
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var connectionString = _container.GetConnectionString();
        ConnectionString = connectionString;

        await using (var bootstrap = NpgsqlDataSource.Create(connectionString))
        await using (var cmd = bootstrap.CreateCommand(
            "CREATE SCHEMA IF NOT EXISTS identity;" +
            "CREATE SCHEMA IF NOT EXISTS applications;" +
            "CREATE SCHEMA IF NOT EXISTS profiles;" +
            "CREATE SCHEMA IF NOT EXISTS notify;"))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        var result = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(MorganHacks.Migrations.MigrationsAssemblyMarker))!)
            .WithTransactionPerScript()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            throw new InvalidOperationException($"Migrations failed: {result.Error}");
        }

        DataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Inserts a person and returns their id.</summary>
    public async Task<Guid> AddPersonAsync(string email, string kind = "hacker")
    {
        await using var cmd = DataSource.CreateCommand(
            "INSERT INTO identity.people (kind, email) VALUES (@kind, @email) RETURNING id");
        cmd.Parameters.AddWithValue("kind", kind);
        cmd.Parameters.AddWithValue("email", email);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Adds a person to a seeded team, optionally with an expiry.</summary>
    public async Task AddToTeamAsync(Guid personId, string slug, DateTimeOffset? expiresAt = null)
    {
        await using var cmd = DataSource.CreateCommand("""
            INSERT INTO identity.team_members (person_id, team_id, expires_at)
            SELECT @personId, id, @expiresAt FROM identity.teams WHERE slug = @slug
            """);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("slug", slug);
        cmd.Parameters.AddWithValue("expiresAt", (object?)expiresAt ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Grants one permission directly to a person.</summary>
    public async Task GrantAsync(Guid personId, string permission, DateTimeOffset? expiresAt = null)
    {
        await using var cmd = DataSource.CreateCommand("""
            INSERT INTO identity.grants (person_id, permission, expires_at)
            VALUES (@personId, @permission, @expiresAt)
            """);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("permission", permission);
        cmd.Parameters.AddWithValue("expiresAt", (object?)expiresAt ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
