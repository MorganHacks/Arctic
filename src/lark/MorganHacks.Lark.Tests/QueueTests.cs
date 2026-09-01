using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Tests;

public class QueueTests(NotifyDatabase db) : IClassFixture<NotifyDatabase>
{
    private MessageQueue Queue => new(db.DataSource);
    private static string Email(string p) => $"{p}-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Two_workers_claim_disjoint_batches()
    {
        // SKIP LOCKED is the reason this needs no separate queue system and no
        // coordination between workers.
        var campaign = await db.AddCampaignAsync();
        for (var i = 0; i < 20; i++)
        {
            await db.QueueAsync(campaign, Email("batch"));
        }

        var claims = await Task.WhenAll(
            Queue.ClaimAsync("worker-a", 10),
            Queue.ClaimAsync("worker-b", 10),
            Queue.ClaimAsync("worker-c", 10));

        var all = claims.SelectMany(c => c.Select(m => m.Id)).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public async Task Transactional_is_claimed_before_broadcast()
    {
        // A login link must never queue behind two thousand announcements.
        var campaign = await db.AddCampaignAsync();
        for (var i = 0; i < 5; i++)
        {
            await db.QueueAsync(campaign, Email("blast"), priority: 10);
        }

        var urgent = await db.QueueAsync(campaign, Email("login"), priority: 0);

        var claimed = await Queue.ClaimAsync("worker", 1);

        Assert.Equal(urgent, Assert.Single(claimed).Id);
    }

    [Fact]
    public async Task Queueing_the_same_person_twice_conflicts_instead_of_double_sending()
    {
        // The duplicate-prevention story, enforced by the database rather than
        // by remembering to check.
        var campaign = await db.AddCampaignAsync();
        var email = Email("dupe");

        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO notify.messages
              (campaign_id, person_id, to_email, rendered_subject,
               rendered_body_html, rendered_body_text)
            VALUES (@c, @p, @e, 's', 'h', 't')
            """);
        var personId = await db.AddPersonAsync(email);
        cmd.Parameters.AddWithValue("c", campaign);
        cmd.Parameters.AddWithValue("p", personId);
        cmd.Parameters.AddWithValue("e", email);
        await cmd.ExecuteNonQueryAsync();

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task A_temporary_failure_is_retried_later()
    {
        var campaign = await db.AddCampaignAsync();
        var id = await db.QueueAsync(campaign, Email("temp"));
        await Queue.ClaimAsync("worker", 10);

        await Queue.RecordFailureAsync(id, FailureClass.Temporary, "greylisted");

        var state = await db.StateOf(id);
        Assert.Equal("pending", state.Status);
        Assert.Equal(1, state.Attempts);
        Assert.NotNull(state.NextAttempt);
    }

    [Fact]
    public async Task A_hard_bounce_is_never_retried_and_suppresses_the_address()
    {
        // Retrying a hard bounce is how a sending domain gets blocked.
        var campaign = await db.AddCampaignAsync();
        var email = Email("bounce");
        var id = await db.QueueAsync(campaign, email);
        await Queue.ClaimAsync("worker", 10);

        await Queue.RecordFailureAsync(id, FailureClass.PermanentAndSuppress, "550 no such user");

        var state = await db.StateOf(id);
        Assert.Equal("failed_perm", state.Status);
        Assert.Null(state.NextAttempt);
        Assert.True(await Queue.IsSuppressedAsync(email, transactional: true));
    }

    [Fact]
    public async Task Our_own_render_error_stops_the_message_but_does_not_suppress_them()
    {
        // Suppressing for our bug would silently stop mailing someone because
        // of a mistake we made.
        var campaign = await db.AddCampaignAsync();
        var email = Email("ourfault");
        var id = await db.QueueAsync(campaign, email);
        await Queue.ClaimAsync("worker", 10);

        await Queue.RecordFailureAsync(id, FailureClass.PermanentOurFault, "render failed");

        Assert.Equal("failed_perm", (await db.StateOf(id)).Status);
        Assert.False(await Queue.IsSuppressedAsync(email, transactional: true));
    }

    [Fact]
    public async Task A_message_gives_up_after_five_attempts()
    {
        var campaign = await db.AddCampaignAsync();
        var id = await db.QueueAsync(campaign, Email("giveup"));

        for (var i = 0; i < RetrySchedule.MaxAttempts; i++)
        {
            await Queue.RecordFailureAsync(id, FailureClass.Temporary, "timeout");
        }

        var state = await db.StateOf(id);
        Assert.Equal("failed_perm", state.Status);
        Assert.Equal(RetrySchedule.MaxAttempts, state.Attempts);
    }

    [Fact]
    public async Task An_unsubscribe_stops_broadcast_but_not_a_login_link()
    {
        // Someone who opted out of announcements still asked for their login
        // link by acting.
        var email = Email("unsub");
        await Queue.SuppressAsync(email, "unsubscribed");

        Assert.True(await Queue.IsSuppressedAsync(email, transactional: false));
        Assert.False(await Queue.IsSuppressedAsync(email, transactional: true));
    }

    [Fact]
    public async Task A_hard_bounce_stops_both_lanes()
    {
        // A dead address is dead either way.
        var email = Email("dead");
        await Queue.SuppressAsync(email, "hard_bounce");

        Assert.True(await Queue.IsSuppressedAsync(email, transactional: false));
        Assert.True(await Queue.IsSuppressedAsync(email, transactional: true));
    }

    [Fact]
    public async Task A_worker_that_dies_mid_claim_has_its_work_recovered()
    {
        // Without the sweeper those rows sit in 'sending' forever and nobody
        // notices until an applicant asks why they never got their decision.
        var campaign = await db.AddCampaignAsync();
        var id = await db.QueueAsync(campaign, Email("orphan"));
        await Queue.ClaimAsync("doomed-worker", 10);
        await db.ExpireLockAsync(id);

        var recovered = await Queue.SweepExpiredLocksAsync();

        Assert.True(recovered >= 1);
        Assert.Equal("pending", (await db.StateOf(id)).Status);
    }

    [Fact]
    public async Task A_claimed_message_is_not_claimed_again()
    {
        var campaign = await db.AddCampaignAsync();
        await db.QueueAsync(campaign, Email("once"));

        var first = await Queue.ClaimAsync("worker-a", 50);
        var second = await Queue.ClaimAsync("worker-b", 50);

        Assert.DoesNotContain(second, m => first.Any(f => f.Id == m.Id));
    }
}

public class FailureClassificationTests
{
    [Theory]
    [InlineData(550, "no such user")]
    [InlineData(null, "domain not found")]
    [InlineData(null, "mailbox unavailable")]
    [InlineData(null, "spam report")]
    public void Addresses_that_will_never_work_are_suppressed(int? status, string message)
    {
        Assert.Equal(FailureClass.PermanentAndSuppress,
            DeliveryFailure.Classify(status, null, message));
    }

    [Theory]
    [InlineData(452, "mailbox full")]
    [InlineData(451, "greylisted, try again")]
    [InlineData(429, "rate exceeded")]
    [InlineData(null, "connection timed out")]
    public void Failures_that_may_clear_are_retried(int? status, string message)
    {
        Assert.Equal(FailureClass.Temporary, DeliveryFailure.Classify(status, null, message));
    }

    [Fact]
    public void Our_own_bug_is_terminal_but_never_suppresses_the_recipient()
    {
        Assert.Equal(FailureClass.PermanentOurFault,
            DeliveryFailure.Classify(null, null, "render failed for template"));
    }

    [Fact]
    public void An_unrecognised_failure_is_retried_rather_than_suppressed()
    {
        // Wrongly retrying costs a few attempts. Wrongly suppressing means
        // somebody silently never hears from us again.
        Assert.Equal(FailureClass.Temporary,
            DeliveryFailure.Classify(null, null, "something nobody has seen before"));
    }

    [Fact]
    public void Throttling_is_recognised_so_the_worker_can_slow_down()
    {
        Assert.True(DeliveryFailure.IsThrottle(429, null, "Maximum sending rate exceeded"));
        Assert.False(DeliveryFailure.IsThrottle(550, null, "no such user"));
    }
}

public class RetryScheduleTests
{
    [Fact]
    public void Delays_are_bounded_by_the_published_ceilings()
    {
        TimeSpan[] ceilings =
        [
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(2), TimeSpan.FromHours(6),
        ];

        for (var attempt = 1; attempt <= RetrySchedule.MaxAttempts; attempt++)
        {
            for (var i = 0; i < 200; i++)
            {
                var delay = RetrySchedule.DelayFor(attempt)!.Value;
                Assert.InRange(delay, TimeSpan.Zero, ceilings[attempt - 1]);
            }
        }
    }

    [Fact]
    public void Delays_are_jittered_rather_than_fixed()
    {
        // Without jitter a thousand messages failing on one throttle all retry
        // in the same second and throttle again.
        var seen = new HashSet<double>();
        for (var i = 0; i < 100; i++)
        {
            seen.Add(RetrySchedule.DelayFor(3)!.Value.TotalMilliseconds);
        }

        Assert.True(seen.Count > 90, $"expected spread, saw {seen.Count} distinct delays");
    }

    [Fact]
    public void Past_the_last_attempt_there_is_no_next_one()
    {
        Assert.Null(RetrySchedule.DelayFor(RetrySchedule.MaxAttempts + 1));
        Assert.Null(RetrySchedule.DelayFor(0));
    }
}
