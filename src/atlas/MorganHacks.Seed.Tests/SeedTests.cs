using DbUp;
using MorganHacks.Applications.Data;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using MorganHacks.Seed;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class SeedTests
{
    [Theory]
    [InlineData("ARCTIC_TARGET", "production")]
    [InlineData("ARCTIC_TARGET", "staging")]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Production")]
    [InlineData("DOTNET_ENVIRONMENT", "Staging")]
    [InlineData("ARCTIC_DB", "Host=localhost;Database=production")]
    public void Refuses_nonlocal_environment(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => SeedSafety.CheckEnvironment(k => k == key ? value : null));

    [Fact]
    public void Allows_local_environment() => SeedSafety.CheckEnvironment(k => k switch
    {
        "ARCTIC_TARGET" => "local",
        "ASPNETCORE_ENVIRONMENT" or "DOTNET_ENVIRONMENT" => "Development",
        _ => null,
    });

    [Fact]
    public void All_fixtures_pass_real_form_validation()
    {
        Assert.Empty(FormValidation.Check(SeedRunner.Fields));
        for (var i = 1; i <= 1000; i++)
        {
            Assert.Empty(SubmissionValidation.Check(SeedRunner.Fields, SeedRunner.Answers(i)));
            Assert.EndsWith("@example.com", SeedRunner.Email(i));
        }
        Assert.Equal(1000, Enumerable.Range(1, 1000).Select(SeedRunner.Email).Distinct().Count());
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Seeds_real_schema_and_preserves_review_changes_on_rerun()
    {
        await using var db = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await db.StartAsync();
        var migration = DeployChanges.To.PostgresqlDatabase(db.GetConnectionString())
            .WithScriptsEmbeddedInAssembly(typeof(MorganHacks.Migrations.MigrationsAssemblyMarker).Assembly)
            .WithTransactionPerScript().Build().PerformUpgrade();
        Assert.True(migration.Successful, migration.Error?.Message);
        await using var source = NpgsqlDataSource.Create(db.GetConnectionString());
        // A real-looking application outside the mock event must survive unchanged.
        await using var sentinel = source.CreateCommand("""
            WITH e AS (INSERT INTO applications.events(slug,name) VALUES ('real-event','Existing event') RETURNING id)
            INSERT INTO applications.applications(event_id,email) SELECT id,'existing@example.org' FROM e RETURNING id
            """);
        var sentinelId = (Guid)(await sentinel.ExecuteScalarAsync())!;
        var seed = new SeedRunner(source);
        Assert.Equal(new SeedResult(50, 0), await seed.RunAsync(50));
        await using var query = source.CreateCommand("""
            SELECT count(*),count(DISTINCT status),
                count(*) FILTER (WHERE status <> 'incomplete' AND submitted_at IS NULL),
                count(DISTINCT submitted_at::date)
            FROM applications.applications WHERE event_id=@event
            """);
        query.Parameters.AddWithValue("event", SeedRunner.EventId);
        await using (var reader = await query.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(50L, reader.GetInt64(0));
            Assert.Equal(11L, reader.GetInt64(1));
            Assert.Equal(0L, reader.GetInt64(2));
            Assert.True(reader.GetInt64(3) > 10);
        }
        await using var selected = source.CreateCommand(
            "SELECT id FROM applications.applications WHERE event_id=@event AND email=@email");
        selected.Parameters.AddWithValue("event", SeedRunner.EventId);
        selected.Parameters.AddWithValue("email", SeedRunner.Email(2));
        var id = (Guid)(await selected.ExecuteScalarAsync())!;
        var applications = new PostgresApplicationStore(source);
        await applications.TransitionAsync(id, ApplicationStatus.UnderReview, reason: "Manual simulation decision");
        await using var author = source.CreateCommand(
            "INSERT INTO identity.people(kind,email) VALUES ('organizer','reviewer@example.com') RETURNING id");
        var actor = (Guid)(await author.ExecuteScalarAsync())!;
        var applicants = new PostgresApplicantStore(source);
        await applicants.AddNoteAsync(id, actor, "Keep this manual review note.");
        var history = await applications.HistoryOfAsync(id);
        Assert.Equal(new SeedResult(0, 50), await seed.RunAsync(50));
        Assert.Equal(ApplicationStatus.UnderReview, await applications.StatusOfAsync(id));
        Assert.Equal(history, await applications.HistoryOfAsync(id));
        Assert.Equal("Keep this manual review note.", Assert.Single(await applicants.NotesOfAsync(id)).Body);
        await using var timeline = source.CreateCommand("""
            SELECT count(*) FROM applications.applications a
            JOIN applications.status_history h ON h.application_id=a.id
            WHERE a.event_id=@event AND h.to_status='expired'
                AND NOT (a.decided_at < a.rsvp_deadline AND a.rsvp_deadline < h.created_at)
            """);
        timeline.Parameters.AddWithValue("event", SeedRunner.EventId);
        Assert.Equal(0L, await timeline.ExecuteScalarAsync());
        Assert.Equal(ApplicationStatus.Incomplete, await applications.StatusOfAsync(sentinelId));
        await using var mail = source.CreateCommand("SELECT count(*) FROM notify.messages");
        Assert.Equal(0L, await mail.ExecuteScalarAsync());
        Assert.Equal(new SeedResult(5, 50), await seed.RunAsync(55));
        await using var collision = source.CreateCommand(
            "UPDATE applications.events SET slug='changed-ownership' WHERE id=@id");
        collision.Parameters.AddWithValue("id", SeedRunner.EventId);
        await collision.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => seed.RunAsync(60));
        await using var count = source.CreateCommand("SELECT count(*) FROM applications.applications");
        Assert.Equal(56L, await count.ExecuteScalarAsync());
    }
}
