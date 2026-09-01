using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Tests;

/// <summary>
/// Two rules the queue has to enforce itself rather than trust a caller with:
/// never hand out a message to an address we are forbidden to mail, and never
/// hand out the same message forever.
/// </summary>
public class SuppressionAndRecoveryTests(NotifyDatabase db) : IClassFixture<NotifyDatabase>
{
    private MessageQueue Queue => new(db.DataSource);
    private static string Email(string p) => $"{p}-{Guid.NewGuid():N}@example.com";

    private const short Transactional = 0;
    private const short Broadcast = 10;

    [Fact]
    public async Task A_bounced_address_is_never_claimed()
    {
        // The check belongs in the claim, not in the sender. A suppressed
        // address that stays claimable is one forgotten `if` away from being
        // mailed, and that is how a sending domain gets blocked.
        var campaign = await db.AddCampaignAsync();
        var email = Email("bounced");
        await Queue.SuppressAsync(email, "hard_bounce");
        var id = await db.QueueAsync(campaign, email, Broadcast);

        var claimed = await Queue.ClaimAsync("worker", 50);

        Assert.DoesNotContain(claimed, m => m.Id == id);
    }

    [Fact]
    public async Task An_unsubscribe_does_not_block_a_login_link()
    {
        // Opting out of announcements is not opting out of the link you just
        // asked for by clicking sign in.
        // Two campaigns, because one person gets at most one message per
        // campaign — that unique index is what stops a blast double-sending.
        var email = Email("unsubscribed");
        await Queue.SuppressAsync(email, "unsubscribed");
        var login = await db.QueueAsync(await db.AddCampaignAsync(), email, Transactional);
        var blast = await db.QueueAsync(await db.AddCampaignAsync(), email, Broadcast);

        var claimed = await Queue.ClaimAsync("worker", 50);

        Assert.Contains(claimed, m => m.Id == login);
        Assert.DoesNotContain(claimed, m => m.Id == blast);
    }

    [Fact]
    public async Task Suppressing_an_address_cancels_what_is_already_queued()
    {
        // Skipping them at claim time would leave the rows pending forever,
        // and pending is the queue saying "still owed".
        var campaign = await db.AddCampaignAsync();
        var email = Email("late-bounce");
        var queued = await db.QueueAsync(campaign, email, Broadcast);

        await Queue.SuppressAsync(email, "complaint");

        Assert.Equal("suppressed", (await db.StateOf(queued)).Status);
    }

    [Fact]
    public async Task An_unsubscribe_cancels_the_blast_but_leaves_the_link_pending()
    {
        var email = Email("opting-out");
        var login = await db.QueueAsync(await db.AddCampaignAsync(), email, Transactional);
        var blast = await db.QueueAsync(await db.AddCampaignAsync(), email, Broadcast);

        await Queue.SuppressAsync(email, "unsubscribed");

        Assert.Equal("pending", (await db.StateOf(login)).Status);
        Assert.Equal("suppressed", (await db.StateOf(blast)).Status);
    }

    [Fact]
    public async Task A_message_that_keeps_killing_the_worker_eventually_gives_up()
    {
        // The poison-message loop. Recovering a stranded row without charging
        // an attempt means claim, crash, sweep, claim, forever: MaxAttempts
        // cannot stop it, because that count is only raised by
        // RecordFailureAsync and a dying process never reaches it.
        var campaign = await db.AddCampaignAsync();
        var id = await db.QueueAsync(campaign, Email("poison"));

        for (var round = 0; round < RetrySchedule.MaxAttempts; round++)
        {
            var claimed = await Queue.ClaimAsync("worker", 50);
            Assert.Contains(claimed, m => m.Id == id);

            // The worker dies here: no MarkSent, no RecordFailure.
            await db.ExpireLockAsync(id);
            await Queue.SweepExpiredLocksAsync();
        }

        Assert.Equal("failed_perm", (await db.StateOf(id)).Status);
        Assert.DoesNotContain(await Queue.ClaimAsync("worker", 50), m => m.Id == id);
    }

    [Fact]
    public async Task One_crash_still_gets_the_message_retried()
    {
        // The bound must not turn into giving up on the first blip.
        var campaign = await db.AddCampaignAsync();
        var id = await db.QueueAsync(campaign, Email("blip"));

        await Queue.ClaimAsync("worker", 50);
        await db.ExpireLockAsync(id);
        await Queue.SweepExpiredLocksAsync();

        var state = await db.StateOf(id);
        Assert.Equal("pending", state.Status);
        Assert.Equal(1, state.Attempts);
    }
}
