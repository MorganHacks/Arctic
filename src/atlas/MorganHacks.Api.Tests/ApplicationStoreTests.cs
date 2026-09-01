using MorganHacks.Applications.Data;
using MorganHacks.Applications.Domain;
using Npgsql;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The lifecycle against a real database, because the two rules being tested —
/// that status and history cannot drift apart, and that the database refuses a
/// duplicate — are both things only a database can actually prove.
/// </summary>
public class ApplicationStoreTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>
{
    private PostgresApplicationStore Store => new(db.DataSource);
    private static string Email(string p) => $"{p}-{Guid.NewGuid():N}@example.com";

    /// <summary>An application sitting in review, which is where decisions happen.</summary>
    private async Task<Guid> UnderReview(Guid eventId)
    {
        var id = await Store.StartAsync(eventId, Email("applicant"));
        await db.CompleteAsync(id);
        await Store.TransitionAsync(id, ApplicationStatus.Submitted);
        await Store.TransitionAsync(id, ApplicationStatus.UnderReview);
        return id;
    }

    [Fact]
    public async Task Starting_an_application_records_its_first_step()
    {
        // An application whose trail begins at its second status is one whose
        // trail cannot be trusted.
        var id = await Store.StartAsync(await db.AddEventAsync(), Email("starter"));

        var history = await Store.HistoryOfAsync(id);

        var first = Assert.Single(history);
        Assert.Null(first.From);
        Assert.Equal(ApplicationStatus.Incomplete, first.To);
    }

    [Fact]
    public async Task Every_transition_leaves_a_row_behind()
    {
        var id = await UnderReview(await db.AddEventAsync());

        await Store.TransitionAsync(id, ApplicationStatus.Accepted);

        var history = await Store.HistoryOfAsync(id);
        Assert.Equal(
            [ApplicationStatus.Incomplete, ApplicationStatus.Submitted,
             ApplicationStatus.UnderReview, ApplicationStatus.Accepted],
            history.Select(h => h.To));
    }

    [Fact]
    public async Task A_refused_transition_leaves_nothing_behind()
    {
        // The status change and its history row share one transaction. If a
        // rejected attempt could still write a history row, the trail would
        // record decisions that never happened.
        var id = await UnderReview(await db.AddEventAsync());
        var before = await db.HistoryCountAsync(id);

        await Assert.ThrowsAsync<InvalidTransitionException>(
            () => Store.TransitionAsync(id, ApplicationStatus.CheckedIn));

        Assert.Equal(ApplicationStatus.UnderReview, await Store.StatusOfAsync(id));
        Assert.Equal(before, await db.HistoryCountAsync(id));
    }

    [Fact]
    public async Task A_decision_in_flight_blocks_the_next_one_until_it_lands()
    {
        // A shared queue means two reviewers opening one application is
        // ordinary. Without the row lock both read 'under_review', both find
        // their change legal, and the application ends up accepted with a
        // history row saying it was rejected.
        var id = await UnderReview(await db.AddEventAsync());

        // Reviewer A accepts, and has not committed yet. This is the window.
        await using var connection = await db.DataSource.OpenConnectionAsync();
        await using var accepting = await connection.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand(
            "UPDATE applications.applications SET status = 'accepted' WHERE id = @id",
            connection, accepting))
        {
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // Reviewer B rejects. Legal against 'under_review', which is still
        // what an uncommitted read would show.
        var rejecting = Store.TransitionAsync(id, ApplicationStatus.Rejected);

        // Asserted rather than assumed: two tasks started together will
        // happily run one after the other, and a race test that never races
        // passes for the wrong reason.
        Assert.True(
            await db.WaitForBlockedQueryAsync(),
            "the second decision never blocked, so this proves nothing");

        await accepting.CommitAsync();

        // It re-reads, finds 'accepted', and refuses rather than overwriting.
        await Assert.ThrowsAsync<InvalidTransitionException>(() => rejecting);
        Assert.Equal(ApplicationStatus.Accepted, await Store.StatusOfAsync(id));
    }

    [Fact]
    public async Task Deciding_records_who_decided_and_when()
    {
        // Every one of these is something a caller would eventually forget to
        // set, and a decided_at that disagrees with the status is worse than
        // not having the column.
        var reviewer = await db.AddPersonAsync(Email("reviewer"));
        var id = await UnderReview(await db.AddEventAsync());

        await Store.TransitionAsync(id, ApplicationStatus.Accepted, reviewer);

        var (decidedAt, decidedBy) = await db.DecisionOf(id);
        Assert.NotNull(decidedAt);
        Assert.Equal(reviewer, decidedBy);
    }

    [Fact]
    public async Task A_bulk_action_can_be_found_again_afterwards()
    {
        // The piece people leave out. When someone bulk-accepts four hundred
        // applicants and one was wrong, this is how the rest of that action is
        // found in order to undo it.
        var eventId = await db.AddEventAsync();
        var batch = Guid.NewGuid();
        var first = await UnderReview(eventId);
        var second = await UnderReview(eventId);

        await Store.TransitionAsync(first, ApplicationStatus.Accepted, batchId: batch);
        await Store.TransitionAsync(second, ApplicationStatus.Accepted, batchId: batch);

        foreach (var id in new[] { first, second })
        {
            Assert.Equal(batch, (await Store.HistoryOfAsync(id))[^1].BatchId);
        }
    }

    [Fact]
    public async Task The_system_acts_with_no_name_against_it()
    {
        // The RSVP expiry job has no actor. Putting a person's name on a
        // decision they did not make is worse than leaving it blank.
        var id = await UnderReview(await db.AddEventAsync());
        await Store.TransitionAsync(id, ApplicationStatus.Accepted);

        await Store.TransitionAsync(id, ApplicationStatus.Expired);

        Assert.Null((await Store.HistoryOfAsync(id))[^1].ActorId);
    }

    [Fact]
    public async Task One_address_cannot_apply_twice_to_one_event()
    {
        // The dedupe rule, at the database rather than in code, so it holds
        // regardless of which path did the insert.
        var eventId = await db.AddEventAsync();
        var email = Email("twice");
        await Store.StartAsync(eventId, email);

        var second = await Assert.ThrowsAsync<PostgresException>(
            () => Store.StartAsync(eventId, email));

        Assert.Equal("23505", second.SqlState);
    }

    [Fact]
    public async Task Case_is_not_a_way_around_the_dedupe_rule()
    {
        var eventId = await db.AddEventAsync();
        var email = Email("Casing");
        await Store.StartAsync(eventId, email);

        await Assert.ThrowsAsync<PostgresException>(
            () => Store.StartAsync(eventId, email.ToUpperInvariant()));
    }

    [Fact]
    public async Task The_same_person_can_apply_to_a_different_event()
    {
        // Scoping everything to an event is what stops next year's cycle
        // either wiping this year's data or becoming a second database.
        var email = Email("returning");
        await Store.StartAsync(await db.AddEventAsync(), email);

        var next = await Store.StartAsync(await db.AddEventAsync(), email);

        Assert.NotEqual(Guid.Empty, next);
    }

    [Fact]
    public async Task An_application_cannot_be_submitted_half_finished()
    {
        // Autosave means partial rows are normal, so the columns are nullable.
        // Requiring them from the moment the row stops being a draft is what
        // makes a submitted application missing an MLH-mandated field
        // impossible rather than merely unlikely.
        var id = await Store.StartAsync(await db.AddEventAsync(), Email("partial"));

        var refused = await Assert.ThrowsAsync<PostgresException>(
            () => Store.TransitionAsync(id, ApplicationStatus.Submitted));

        Assert.Equal("23514", refused.SqlState);
        Assert.Contains("submitted_applications_are_complete", refused.ConstraintName);
    }

    [Fact]
    public async Task Someone_who_never_finished_can_still_withdraw()
    {
        // The completeness rule must not trap people who started, changed
        // their mind, and asked to be removed.
        var id = await Store.StartAsync(await db.AddEventAsync(), Email("gave-up"));

        await Store.TransitionAsync(id, ApplicationStatus.Withdrawn);

        Assert.Equal(ApplicationStatus.Withdrawn, await Store.StatusOfAsync(id));
    }
}
