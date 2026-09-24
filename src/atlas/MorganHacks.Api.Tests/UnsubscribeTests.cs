using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Api.Tests;

public class UnsubscribeTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;
    private MessageQueue Queue => new(db.DataSource);

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Opening_is_read_only_and_confirming_stops_only_the_link_recipient_broadcasts()
    {
        var email = $"unsubscribe-{Guid.NewGuid():N}@example.invalid";
        var editor = await db.AddPersonAsync($"editor-{Guid.NewGuid():N}@example.invalid");
        await db.AddToTeamAsync(editor, "comms");
        using var scope = _app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(editor);
        using var author = _app.CreateClient();
        author.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");
        var key = $"unsubscribe-{Guid.NewGuid():N}";
        var created = await author.PostAsJsonAsync("/admin/templates", new
        {
            key,
            kind = "broadcast",
            subject = "Announcements",
            format = "html",
            body = "<p>Content</p><a href='{$unsubscribe_link}'>Unsubscribe</a>",
            fromLocal = "mail",
            fromDomain = "example.invalid",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var template = (await new TemplateStore(db.DataSource).FindAsync(key))!;
        var messageId = await Queue.EnqueueTransactionalAsync(template, email, null, new Dictionary<string, string>());
        var message = Assert.Single(await Queue.ClaimAsync("unsubscribe-test", 50), item => item.Id == messageId);
        var rendered = await new UnsubscribeStore(db.DataSource).PrepareAsync(message, "https://api.example.invalid");
        var path = new Uri(Assert.Single(EmailLinks.Destinations(rendered.BodyHtml))).AbsolutePath;
        Assert.Contains(path, rendered.BodyText);
        await Queue.MarkSentAsync(messageId, "fake-provider-message");
        var pending = await Queue.EnqueueTransactionalAsync(template, email, null, new Dictionary<string, string>());

        using var client = _app.CreateClient();
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        using var opened = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Contains("Unsubscribe from announcements?", await opened.Content.ReadAsStringAsync());
        Assert.DoesNotContain(email, await opened.Content.ReadAsStringAsync());
        Assert.True(opened.Headers.CacheControl!.NoStore);
        Assert.Equal("no-referrer", Assert.Single(opened.Headers.GetValues("Referrer-Policy")));
        Assert.False(await Queue.IsSuppressedAsync(email, false));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, new FormUrlEncodedContent([]))).StatusCode);
        Assert.False(await Queue.IsSuppressedAsync(email, false));

        using var confirmed = await client.PostAsync(path + "?email=someone-else@example.invalid", Confirmation());
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Contains("You are unsubscribed", await confirmed.Content.ReadAsStringAsync());
        Assert.True(await Queue.IsSuppressedAsync(email.ToUpperInvariant(), false));
        Assert.False(await Queue.IsSuppressedAsync(email, true));
        Assert.False(await Queue.IsSuppressedAsync("someone-else@example.invalid", false));
        await using var status = db.DataSource.CreateCommand("SELECT status FROM notify.messages WHERE id = @id");
        status.Parameters.AddWithValue("id", pending);
        Assert.Equal("suppressed", await status.ExecuteScalarAsync());

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(path, Confirmation())).StatusCode);
        Assert.Contains("You are unsubscribed", await client.GetStringAsync(path));
        var later = await Queue.EnqueueTransactionalAsync(template, email, null, new Dictionary<string, string>());
        Assert.DoesNotContain(await Queue.ClaimAsync("after-unsubscribe", 50), item => item.Id == later);
    }

    [Fact]
    public async Task Unknown_links_do_not_accept_an_email_from_the_request()
    {
        using var client = _app.CreateClient();
        var path = $"/email/unsubscribe/{Guid.NewGuid():N}?email=unrelated@example.invalid";
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(path, Confirmation())).StatusCode);
        Assert.False(await Queue.IsSuppressedAsync("unrelated@example.invalid", false));
    }

    [Theory]
    [InlineData("hard_bounce")]
    [InlineData("complaint")]
    public async Task Unsubscribing_does_not_weaken_an_existing_suppression(string reason)
    {
        var email = $"suppressed-{Guid.NewGuid():N}@example.invalid";
        var message = new ClaimedMessage(Guid.NewGuid(), Guid.NewGuid(), email, 10, 0, "Subject",
            "<a href='{$unsubscribe_link}'>Unsubscribe</a>", "{$unsubscribe_link}", "mail@example.invalid", null, null);
        var rendered = await new UnsubscribeStore(db.DataSource).PrepareAsync(message, "https://api.example.invalid");
        var path = new Uri(Assert.Single(EmailLinks.Destinations(rendered.BodyHtml))).AbsolutePath;
        await Queue.SuppressAsync(email, reason);
        using var client = _app.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(path, Confirmation())).StatusCode);
        Assert.True(await Queue.IsSuppressedAsync(email, true));
        await using var read = db.DataSource.CreateCommand("SELECT reason FROM notify.suppressions WHERE email = @email");
        read.Parameters.AddWithValue("email", email);
        Assert.Equal(reason, await read.ExecuteScalarAsync());
    }

    private static FormUrlEncodedContent Confirmation() => new([
        new KeyValuePair<string, string>("confirm", "unsubscribe"),
    ]);
}
