using MorganHacks.Applications.Data;
using MorganHacks.Applications.Domain;
using Npgsql;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The rules that hold however a row is written.
/// </summary>
/// <remarks>
/// Every test here writes raw SQL and never touches the store. That is the
/// entire point: the store was already correct, and "correct as long as
/// everyone goes through the store" is exactly the assumption that fails when
/// somebody fixes one row in psql during the event.
/// </remarks>
public class DatabaseInvariantTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>
{
    private PostgresApplicationStore Store => new(db.DataSource);
    private static string Email(string p) => $"{p}-{Guid.NewGuid():N}@example.com";

    private async Task<Guid> Ready(Guid eventId)
    {
        var id = await Store.StartAsync(eventId, Email("raw"));
        await db.CompleteAsync(id);
        return id;
    }

    /// <summary>A status change written by hand, exactly as an incident fix would be.</summary>
    private async Task RawUpdate(Guid id, string status)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE applications.applications SET status = @s WHERE id = @id");
        cmd.Parameters.AddWithValue("s", status);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task A_status_changed_by_hand_still_lands_in_the_history()
    {
        // The one that matters. Before the trigger this succeeded silently and
        // wrote nothing, which does not leave a gap in the trail — it leaves a
        // trail that is wrong, in a way nobody can detect afterwards.
        var id = await Ready(await db.AddEventAsync());
        var before = await db.HistoryCountAsync(id);

        await RawUpdate(id, "submitted");

        Assert.Equal(before + 1, await db.HistoryCountAsync(id));
        var history = await Store.HistoryOfAsync(id);
        Assert.Equal(ApplicationStatus.Submitted, history[^1].To);
        Assert.Equal(ApplicationStatus.Incomplete, history[^1].From);
    }

    [Fact]
    public async Task A_hand_written_change_records_no_actor()
    {
        // Honest rather than tidy. A row fixed by hand genuinely has nobody
        // behind it, and seeing a null actor is how you know it was manual.
        var id = await Ready(await db.AddEventAsync());

        await RawUpdate(id, "submitted");

        Assert.Null((await Store.HistoryOfAsync(id))[^1].ActorId);
    }

    [Fact]
    public async Task Creating_an_application_records_its_first_status()
    {
        // Written by the trigger now, so it holds for rows the store did not
        // create — a seed script, an import, a migration.
        var eventId = await db.AddEventAsync();
        Guid id;
        await using (var cmd = db.DataSource.CreateCommand(
            "INSERT INTO applications.applications (event_id, email) VALUES (@e, @m) RETURNING id"))
        {
            cmd.Parameters.AddWithValue("e", eventId);
            cmd.Parameters.AddWithValue("m", Email("imported"));
            id = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        var first = Assert.Single(await Store.HistoryOfAsync(id));
        Assert.Null(first.From);
        Assert.Equal(ApplicationStatus.Incomplete, first.To);
    }

    [Fact]
    public async Task A_write_that_does_not_move_the_status_records_nothing()
    {
        // Editing an answer is not a lifecycle event, and a trail full of
        // "unchanged" rows is one nobody reads.
        var id = await Ready(await db.AddEventAsync());
        var before = await db.HistoryCountAsync(id);

        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE applications.applications SET school = 'Somewhere else' WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();

        Assert.Equal(before, await db.HistoryCountAsync(id));
    }

    [Fact]
    public async Task A_decision_made_by_hand_is_still_timestamped()
    {
        var id = await Ready(await db.AddEventAsync());
        await RawUpdate(id, "submitted");
        await RawUpdate(id, "under_review");

        await RawUpdate(id, "accepted");

        var (decidedAt, _) = await db.DecisionOf(id);
        Assert.NotNull(decidedAt);
    }

    [Fact]
    public async Task Updated_at_moves_without_anyone_setting_it()
    {
        var id = await Ready(await db.AddEventAsync());
        var before = await db.UpdatedAtOf(id);

        await RawUpdate(id, "submitted");

        Assert.True(await db.UpdatedAtOf(id) >= before);
    }

    [Fact]
    public async Task The_store_still_records_who_decided()
    {
        // The trigger takes the actor from the transaction, so going through
        // the store must still attribute the decision to a person.
        var reviewer = await db.AddPersonAsync(Email("reviewer"));
        var id = await Ready(await db.AddEventAsync());
        await Store.TransitionAsync(id, ApplicationStatus.Submitted);
        await Store.TransitionAsync(id, ApplicationStatus.UnderReview);

        await Store.TransitionAsync(
            id, ApplicationStatus.Accepted, reviewer, reason: "strong application");

        var last = (await Store.HistoryOfAsync(id))[^1];
        Assert.Equal(reviewer, last.ActorId);
        Assert.Equal("strong application", last.Reason);
        Assert.Equal(reviewer, (await db.DecisionOf(id)).DecidedBy);
    }

    [Fact]
    public async Task An_actor_does_not_leak_onto_the_next_write()
    {
        // The settings are transaction-local. If they were not, the next
        // request borrowing this pooled connection would attribute its
        // decision to whoever happened to use it last.
        var reviewer = await db.AddPersonAsync(Email("reviewer"));
        var eventId = await db.AddEventAsync();

        var first = await Ready(eventId);
        await Store.TransitionAsync(first, ApplicationStatus.Submitted, reviewer);

        var second = await Ready(eventId);
        await RawUpdate(second, "submitted");

        Assert.Null((await Store.HistoryOfAsync(second))[^1].ActorId);
    }
}

/// <summary>The MLH export, where the consent filter cannot be forgotten.</summary>
public class MlhExportTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>
{
    private PostgresApplicationStore Store => new(db.DataSource);
    private static string Email(string p) => $"{p}-{Guid.NewGuid():N}@example.com";

    private async Task<bool> InExport(Guid id)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM applications.mlh_export WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    [Fact]
    public async Task An_application_cannot_be_submitted_without_the_agreement()
    {
        // The first line of defence, and the stronger one: a submitted
        // applicant who never agreed to data sharing cannot exist at all, so
        // the export has nothing to accidentally include. MLH makes that
        // checkbox required, and the completeness constraint enforces it.
        var id = await Store.StartAsync(await db.AddEventAsync(), Email("no-consent"));
        await db.CompleteAsync(id, dataSharing: false);

        var refused = await Assert.ThrowsAsync<PostgresException>(
            () => Store.TransitionAsync(id, ApplicationStatus.Submitted));

        Assert.Equal("23514", refused.SqlState);
    }

    [Fact]
    public async Task A_row_without_the_agreement_is_not_reachable_through_the_export()
    {
        // Second line, and deliberately belt and braces. Today the constraint
        // above already makes this unreachable; the view stays correct if that
        // checkbox ever becomes optional, and means the consent filter is
        // something you select from rather than something you remember.
        var id = await Store.StartAsync(await db.AddEventAsync(), Email("no-consent"));
        await db.CompleteAsync(id, dataSharing: false);

        Assert.False(await InExport(id));
    }

    [Fact]
    public async Task Somebody_who_agreed_is_included()
    {
        var id = await Store.StartAsync(await db.AddEventAsync(), Email("consented"));
        await db.CompleteAsync(id);
        await Store.TransitionAsync(id, ApplicationStatus.Submitted);

        Assert.True(await InExport(id));
    }

    [Fact]
    public async Task An_unfinished_application_is_not_a_registrant()
    {
        var id = await Store.StartAsync(await db.AddEventAsync(), Email("half-done"));
        await db.CompleteAsync(id);

        Assert.False(await InExport(id));
    }

    [Fact]
    public async Task Somebody_who_withdrew_is_not_a_registrant()
    {
        var id = await Store.StartAsync(await db.AddEventAsync(), Email("withdrawn"));
        await db.CompleteAsync(id);
        await Store.TransitionAsync(id, ApplicationStatus.Submitted);
        await Store.TransitionAsync(id, ApplicationStatus.Withdrawn);

        Assert.False(await InExport(id));
    }
}
