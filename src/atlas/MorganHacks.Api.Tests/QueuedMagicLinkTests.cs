using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Requesting a sign-in link, all the way to a row lark can claim.
/// </summary>
/// <remarks>
/// Atlas does not send mail. What it must do is leave a correctly rendered,
/// correctly prioritised message behind, and that is the seam worth testing —
/// no provider is involved on either side of it.
/// </remarks>
public class QueuedMagicLinkTests(IdentityDatabase db)
    : IClassFixture<IdentityDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(
            b => b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    private static string Unique() => $"queued-{Guid.NewGuid():N}@example.com";

    private async Task<(string Subject, string Html, string Text, short Priority, Guid? Person)>
        QueuedFor(string email)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT rendered_subject, rendered_body_html, rendered_body_text,
                   priority, person_id
              FROM notify.messages
             WHERE to_email = @email
             ORDER BY created_at DESC
             LIMIT 1
            """);
        cmd.Parameters.AddWithValue("email", email);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync(), "nothing was queued for that address");
        return (r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt16(3),
                await r.IsDBNullAsync(4) ? null : r.GetGuid(4));
    }

    [Fact]
    public async Task Requesting_a_link_leaves_a_message_for_lark()
    {
        var email = Unique();
        var personId = await db.AddPersonAsync(email);

        var response = await _app.CreateClient()
            .PostAsJsonAsync("/auth/magic-link", new { email });
        response.EnsureSuccessStatusCode();

        var queued = await QueuedFor(email);
        Assert.Equal(personId, queued.Person);
        Assert.Contains("sign-in link", queued.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_queued_message_carries_a_usable_link()
    {
        // Rendered at queue time, so what is stored is what gets sent. A
        // placeholder surviving into the row means nobody can sign in.
        var email = Unique();
        await db.AddPersonAsync(email);

        await _app.CreateClient().PostAsJsonAsync("/auth/magic-link", new { email });

        var queued = await QueuedFor(email);
        Assert.DoesNotContain("{{", queued.Html);
        Assert.DoesNotContain("{{", queued.Text);
        Assert.Contains("/auth/consume?token=", queued.Text);
    }

    [Fact]
    public async Task A_sign_in_link_outranks_every_announcement()
    {
        // Priority 0. Somebody waiting to log in must never queue behind two
        // thousand broadcast emails.
        var email = Unique();
        await db.AddPersonAsync(email);

        await _app.CreateClient().PostAsJsonAsync("/auth/magic-link", new { email });

        Assert.Equal(0, (await QueuedFor(email)).Priority);
    }

    [Fact]
    public async Task An_unknown_address_queues_nothing_at_all()
    {
        // The endpoint answers identically either way, but it must not leave
        // a message behind for somebody who has no account.
        var email = Unique();

        var response = await _app.CreateClient()
            .PostAsJsonAsync("/auth/magic-link", new { email });
        response.EnsureSuccessStatusCode();

        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM notify.messages WHERE to_email = @email");
        cmd.Parameters.AddWithValue("email", email);
        Assert.Equal(0L, await cmd.ExecuteScalarAsync());
    }
}
