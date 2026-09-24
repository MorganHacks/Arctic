using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Tests;

public class UnsubscribeTests(NotifyDatabase db) : IClassFixture<NotifyDatabase>
{
    [Fact]
    public async Task Recipient_links_are_private_stable_and_separate_from_click_tracking()
    {
        var email = $"unsubscribe-{Guid.NewGuid():N}@example.invalid";
        var campaign = await db.AddCampaignAsync("broadcast");
        var id = await db.QueueAsync(campaign, email);
        var message = new ClaimedMessage(id, campaign, email, 10, 0, "Subject",
            "<a href='https://example.invalid/event'>Event</a><a href='{$unsubscribe_link}'>Unsubscribe</a>",
            "Event <https://example.invalid/event> Unsubscribe <{$unsubscribe_link}>",
            "mail@example.invalid", null, null, ClickTracking: true);
        var tracking = new LinkTrackingStore(db.DataSource);
        var store = new UnsubscribeStore(db.DataSource);
        var tracked = await tracking.PrepareAsync(message, "https://api.example.invalid/api");
        var prepared = await store.PrepareAsync(tracked, "https://api.example.invalid/api/");
        var retry = await store.PrepareAsync(tracked with { ToEmail = email.ToUpperInvariant() }, "https://api.example.invalid/api");
        var url = Assert.Single(EmailLinks.Destinations(prepared.BodyHtml), link => link.Contains("/unsubscribe/"));

        Assert.DoesNotContain(email, url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(EmailUnsubscribe.Placeholder, prepared.BodyHtml);
        Assert.DoesNotContain(EmailUnsubscribe.Placeholder, prepared.BodyText);
        Assert.Contains(url, prepared.BodyText);
        Assert.Equal(prepared.BodyHtml, retry.BodyHtml);
        Assert.Contains("/email/click/", prepared.BodyHtml);
        var token = Guid.Parse(new Uri(url).Segments.Last());
        Assert.NotEqual(id, token);
        Assert.Equal(email, await store.FindEmailAsync(token));
        Assert.Null(await store.FindEmailAsync(Guid.NewGuid()));

        await using var read = db.DataSource.CreateCommand("SELECT count(*) FROM notify.tracked_links WHERE message_id = @id");
        read.Parameters.AddWithValue("id", id);
        Assert.Equal(1L, await read.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Messages_without_the_marker_need_no_unsubscribe_configuration()
    {
        var message = new ClaimedMessage(Guid.NewGuid(), Guid.NewGuid(), "no-link@example.invalid", 0, 0,
            "Sign in", "<p>Your sign-in link</p>", "Your sign-in link", "mail@example.invalid", null, null);
        Assert.Equal(message, await new UnsubscribeStore(db.DataSource).PrepareAsync(message, ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://example.invalid")]
    [InlineData("https://user:password@example.invalid")]
    [InlineData("https://example.invalid?query=value")]
    [InlineData("https://example.invalid#fragment")]
    [InlineData("javascript:alert(1)")]
    public async Task Invalid_public_addresses_refuse_to_prepare_the_message(string baseUrl)
    {
        var message = new ClaimedMessage(Guid.NewGuid(), Guid.NewGuid(), "refuse@example.invalid", 10, 0,
            "Subject", "<p>Body</p>", EmailUnsubscribe.Placeholder, "mail@example.invalid", null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new UnsubscribeStore(db.DataSource).PrepareAsync(message, baseUrl));
    }

    [Fact]
    public async Task A_later_bounce_still_blocks_sign_in_mail_after_an_unsubscribe()
    {
        var email = $"bounce-{Guid.NewGuid():N}@example.invalid";
        var queue = new MessageQueue(db.DataSource);
        await queue.SuppressAsync(email, "unsubscribed");
        Assert.False(await queue.IsSuppressedAsync(email, true));

        await queue.SuppressAsync(email.ToUpperInvariant(), "hard_bounce");
        await queue.SuppressAsync(email, "unsubscribed");

        Assert.True(await queue.IsSuppressedAsync(email, true));
        Assert.True(await queue.IsSuppressedAsync(email, false));
    }

    [Fact]
    public async Task An_unsubscribe_stops_already_claimed_broadcasts_but_keeps_sign_in_emails()
    {
        var email = $"claimed-{Guid.NewGuid():N}@example.invalid";
        var queue = new MessageQueue(db.DataSource);
        var broadcast = await db.QueueAsync(await db.AddCampaignAsync("broadcast"), email);
        var signIn = await db.QueueAsync(await db.AddCampaignAsync(), email, 0);
        var claimed = await queue.ClaimAsync("unsubscribe-test", 50);
        Assert.Contains(claimed, message => message.Id == broadcast);
        Assert.Contains(claimed, message => message.Id == signIn);

        await queue.SuppressAsync(email, "unsubscribed");

        Assert.True(await queue.StopIfSuppressedAsync(broadcast));
        Assert.False(await queue.StopIfSuppressedAsync(signIn));
        Assert.Equal("suppressed", (await db.StateOf(broadcast)).Status);
        Assert.Equal("sending", (await db.StateOf(signIn)).Status);
    }
}
