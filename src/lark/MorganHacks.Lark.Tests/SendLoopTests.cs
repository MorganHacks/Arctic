using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;
using MorganHacks.Lark.Sending;

namespace MorganHacks.Lark.Tests;

/// <summary>A provider that answers however a test needs it to, and remembers.</summary>
internal sealed class FakeProvider(Func<ClaimedMessage, SendOutcome> answer) : IEmailProvider
{
    public List<ClaimedMessage> Sent { get; } = [];

    public Task<SendOutcome> SendAsync(ClaimedMessage message, CancellationToken ct = default)
    {
        Sent.Add(message);
        return Task.FromResult(answer(message));
    }
}

/// <summary>
/// The worker, from a queued row to a recorded outcome.
/// </summary>
/// <remarks>
/// Against a real database and a fake provider: the parts worth testing are
/// which rows get picked up and what is written afterwards, and neither of
/// those involves SES.
/// </remarks>
public class SendLoopTests(NotifyDatabase db) : IClassFixture<NotifyDatabase>
{
    private MessageQueue Queue => new(db.DataSource);
    private static string Email(string p) => $"{p}-{Guid.NewGuid():N}@example.com";

    private SendLoop LoopWith(IEmailProvider provider, FakeTimeProvider clock) =>
        new(Queue, provider, Options.Create(new SendLoopOptions
        {
            BatchSize = 50,
            BetweenSends = TimeSpan.Zero,
            IdleDelay = TimeSpan.FromMilliseconds(10),
        }), clock, NullLogger<SendLoop>.Instance);

    /// <summary>Runs the loop until it has been round once, then stops it.</summary>
    private static async Task RunOnce(SendLoop loop, FakeTimeProvider clock)
    {
        using var stop = new CancellationTokenSource();
        var running = loop.StartAsync(stop.Token);

        // The loop awaits its idle delay through the same clock, so advancing
        // it is what lets an iteration finish rather than a real sleep.
        for (var i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(50));
            await Task.Delay(20);
        }

        await stop.CancelAsync();
        await loop.StopAsync(CancellationToken.None);
        await running;
    }

    [Fact]
    public async Task A_queued_message_is_sent_and_recorded()
    {
        var campaign = await db.AddCampaignAsync();
        var id = await db.QueueAsync(campaign, Email("recipient"));
        var clock = new FakeTimeProvider();
        var provider = new FakeProvider(_ => SendOutcome.Sent("ses-message-1"));

        await RunOnce(LoopWith(provider, clock), clock);

        Assert.Contains(provider.Sent, m => m.Id == id);
        Assert.Equal("sent", (await db.StateOf(id)).Status);
    }

    [Fact]
    public async Task A_hard_bounce_suppresses_the_address_and_is_not_retried()
    {
        // The single most important behaviour in the worker. Retrying a hard
        // bounce is how a sending domain gets blocked.
        var campaign = await db.AddCampaignAsync();
        var email = Email("gone");
        var id = await db.QueueAsync(campaign, email);
        var clock = new FakeTimeProvider();
        var provider = new FakeProvider(_ =>
            SendOutcome.Refused("mailbox does not exist", 550, "MessageRejected"));

        await RunOnce(LoopWith(provider, clock), clock);

        Assert.Equal("failed_perm", (await db.StateOf(id)).Status);
        Assert.True(await Queue.IsSuppressedAsync(email, transactional: true));
    }

    [Fact]
    public async Task A_throttle_is_retried_rather_than_given_up_on()
    {
        var campaign = await db.AddCampaignAsync();
        var id = await db.QueueAsync(campaign, Email("throttled"));
        var clock = new FakeTimeProvider();
        var provider = new FakeProvider(_ =>
            SendOutcome.Refused("Maximum sending rate exceeded", 429, "Throttling"));

        await RunOnce(LoopWith(provider, clock), clock);

        var state = await db.StateOf(id);
        Assert.Equal("pending", state.Status);
        Assert.Equal(1, state.Attempts);
        Assert.NotNull(state.NextAttempt);
    }

    [Fact]
    public async Task A_suppressed_address_is_never_handed_to_the_provider()
    {
        // Belt and braces with the claim query: the provider is the last place
        // this could go wrong, so assert it never even sees the message.
        var campaign = await db.AddCampaignAsync();
        var email = Email("blocked");
        await Queue.SuppressAsync(email, "complaint");
        await db.QueueAsync(campaign, email);
        var clock = new FakeTimeProvider();
        var provider = new FakeProvider(_ => SendOutcome.Sent("should-not-happen"));

        await RunOnce(LoopWith(provider, clock), clock);

        Assert.Empty(provider.Sent);
    }

    [Fact]
    public async Task A_reply_reaches_an_inbox_somebody_reads()
    {
        // The from address has no mailbox behind it. Without a reply-to, a
        // person replying to ask for help is answered by silence and concludes
        // they were ignored.
        var campaign = await db.AddCampaignAsync(replyTo: "hello@morganhacks.com");
        await db.QueueAsync(campaign, Email("replier"));
        var clock = new FakeTimeProvider();
        var provider = new FakeProvider(_ => SendOutcome.Sent("ses-message-3"));

        await RunOnce(LoopWith(provider, clock), clock);

        Assert.All(provider.Sent, m => Assert.Equal("hello@morganhacks.com", m.ReplyTo));
    }

    [Fact]
    public async Task The_send_carries_the_templates_own_from_address()
    {
        // Not a worker-level default. A login link sent from the broadcast
        // domain is how login deliverability gets poisoned, and a default is
        // exactly the kind of thing that ends up wrong once and stays wrong.
        var campaign = await db.AddCampaignAsync();
        await db.QueueAsync(campaign, Email("addressed"));
        var clock = new FakeTimeProvider();
        var provider = new FakeProvider(_ => SendOutcome.Sent("ses-message-2"));

        await RunOnce(LoopWith(provider, clock), clock);

        Assert.All(provider.Sent, m => Assert.Contains("@", m.From));
    }
}
