using System.Reflection;
using DbUp;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MorganHacks.Api.Tests;

/// <summary>
/// A throwaway Postgres carrying the real applications schema.
/// </summary>
/// <remarks>
/// Built by running the migrations rather than a hand-written setup script, so
/// the constraints under test are the ones that will actually be in production
/// — the dedupe index and the completeness check are half the point of these
/// tests, and a hand-rolled schema would quietly not have them.
/// </remarks>
public sealed class ApplicationsDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:18-alpine").Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var connectionString = _container.GetConnectionString();

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

    public async Task<Guid> AddEventAsync(string? slug = null)
    {
        await using var cmd = DataSource.CreateCommand(
            "INSERT INTO applications.events (slug, name) VALUES (@s, 'Test event') RETURNING id");
        cmd.Parameters.AddWithValue("s", slug ?? $"event-{Guid.NewGuid():N}");
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<Guid> AddPersonAsync(string email)
    {
        await using var cmd = DataSource.CreateCommand(
            "INSERT INTO identity.people (kind, email) VALUES ('organizer', @e) RETURNING id");
        cmd.Parameters.AddWithValue("e", email);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Fills in the fields a submitted application is required to have.</summary>
    public async Task CompleteAsync(Guid applicationId)
    {
        await using var cmd = DataSource.CreateCommand("""
            UPDATE applications.applications
               SET first_name = 'Ada', last_name = 'Lovelace', age = 20,
                   phone = '+15550000000', school = 'Morgan State University',
                   level_of_study = 'undergraduate', country = 'United States',
                   mlh_coc_agreed_at = now(), mlh_data_sharing_at = now()
             WHERE id = @id
            """);
        cmd.Parameters.AddWithValue("id", applicationId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Waits until some query is stuck waiting on a lock, or gives up.
    /// </summary>
    /// <remarks>
    /// Lets a concurrency test assert that the second writer is actually
    /// blocked rather than assuming two tasks overlapped. Two calls started
    /// together will often just run one after the other, and a race test that
    /// never races passes for the wrong reason.
    /// </remarks>
    public async Task<bool> WaitForBlockedQueryAsync(TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            await using var cmd = DataSource.CreateCommand(
                "SELECT count(*) FROM pg_locks WHERE NOT granted");
            if ((long)(await cmd.ExecuteScalarAsync())! > 0)
            {
                return true;
            }

            await Task.Delay(25);
        }

        return false;
    }

    public async Task<int> HistoryCountAsync(Guid applicationId)
    {
        await using var cmd = DataSource.CreateCommand(
            "SELECT count(*) FROM applications.status_history WHERE application_id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<(DateTimeOffset? DecidedAt, Guid? DecidedBy)> DecisionOf(Guid applicationId)
    {
        await using var cmd = DataSource.CreateCommand(
            "SELECT decided_at, decided_by FROM applications.applications WHERE id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (await r.IsDBNullAsync(0) ? null : r.GetFieldValue<DateTimeOffset>(0),
                await r.IsDBNullAsync(1) ? null : r.GetGuid(1));
    }
}
