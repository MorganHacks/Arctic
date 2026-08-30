using MorganHacks.Identity.Data;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Behaviour that only a real database can demonstrate: atomicity, and the
/// exact moment a token stops working.
/// </summary>
public class IdentityStoreTests(IdentityDatabase db) : IClassFixture<IdentityDatabase>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private PostgresIdentityStore Store => new(db.DataSource);

    private (MagicLinkService Links, SessionService Sessions) ServicesAt(DateTimeOffset now)
    {
        var clock = new FakeClock(now);
        return (new MagicLinkService(Store, clock), new SessionService(Store, clock));
    }

    // ------------------------------------------------------------- magic links

    [Fact]
    public async Task A_magic_link_logs_the_right_person_in()
    {
        var personId = await db.AddPersonAsync($"hacker-{Guid.NewGuid():N}@example.com");
        var (links, _) = ServicesAt(Now);

        var token = await links.IssueAsync($"hacker-{personId}@example.com");
        Assert.Null(token); // unknown address

        var email = await EmailOf(personId);
        token = await links.IssueAsync(email);
        Assert.NotNull(token);

        var result = await links.ConsumeAsync(token);

        Assert.True(result.Accepted);
        Assert.Equal(personId, result.PersonId);
    }

    [Fact]
    public async Task An_unknown_address_yields_no_token_and_no_error()
    {
        // The endpoint above this must answer identically either way. Throwing
        // here would make that impossible to do without a try/catch that some
        // future refactor forgets.
        var (links, _) = ServicesAt(Now);

        Assert.Null(await links.IssueAsync("nobody@example.com"));
    }

    [Fact]
    public async Task A_magic_link_works_exactly_once()
    {
        var personId = await db.AddPersonAsync($"once-{Guid.NewGuid():N}@example.com");
        var (links, _) = ServicesAt(Now);
        var token = await links.IssueAsync(await EmailOf(personId));

        var first = await links.ConsumeAsync(token!);
        var second = await links.ConsumeAsync(token!);

        Assert.True(first.Accepted);
        Assert.False(second.Accepted);
        Assert.Equal(TokenRejection.AlreadyConsumed, second.Rejection);
    }

    [Fact]
    public async Task Two_simultaneous_clicks_produce_exactly_one_login()
    {
        // Mail clients and corporate link scanners prefetch URLs, so the first
        // "click" is often a machine and the human's click arrives while it is
        // still in flight. If consumption were a check followed by a write,
        // both would succeed and one token would mint two sessions.
        var personId = await db.AddPersonAsync($"race-{Guid.NewGuid():N}@example.com");
        var (links, _) = ServicesAt(Now);
        var token = await links.IssueAsync(await EmailOf(personId));

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => links.ConsumeAsync(token!)));

        Assert.Equal(1, attempts.Count(a => a.Accepted));
        Assert.All(
            attempts.Where(a => !a.Accepted),
            a => Assert.Equal(TokenRejection.AlreadyConsumed, a.Rejection));
    }

    [Fact]
    public async Task A_magic_link_expires_after_fifteen_minutes()
    {
        var personId = await db.AddPersonAsync($"expiry-{Guid.NewGuid():N}@example.com");
        var (issuer, _) = ServicesAt(Now);
        var token = await issuer.IssueAsync(await EmailOf(personId));

        var (justInside, _) = ServicesAt(Now.Add(MagicLinkService.Lifetime).AddSeconds(-1));
        var (justOutside, _) = ServicesAt(Now.Add(MagicLinkService.Lifetime).AddSeconds(1));

        Assert.False((await justOutside.ConsumeAsync(token!)).Accepted);
        Assert.True((await justInside.ConsumeAsync(token!)).Accepted);
    }

    [Fact]
    public async Task An_invented_token_is_rejected()
    {
        var (links, _) = ServicesAt(Now);
        var (raw, _) = SecureToken.Issue();

        var result = await links.ConsumeAsync(raw);

        Assert.Equal(TokenRejection.NotFound, result.Rejection);
    }

    // ---------------------------------------------------------------- sessions

    [Fact]
    public async Task A_session_validates_until_it_is_revoked()
    {
        var personId = await db.AddPersonAsync($"session-{Guid.NewGuid():N}@example.com");
        var (_, sessions) = ServicesAt(Now);

        var token = await sessions.StartAsync(personId, "test-agent", "203.0.113.10");
        Assert.True((await sessions.ValidateAsync(token)).Accepted);

        await sessions.RevokeAsync(token);

        var afterRevoke = await sessions.ValidateAsync(token);
        Assert.False(afterRevoke.Accepted);
        Assert.Equal(TokenRejection.Revoked, afterRevoke.Rejection);
    }

    [Fact]
    public async Task Revoking_a_person_ends_every_session_they_hold()
    {
        // This is what "remove their access" has to mean. An organizer who
        // leaves badly must not keep an open laptop that can still export the
        // applicant list.
        var personId = await db.AddPersonAsync($"multi-{Guid.NewGuid():N}@example.com");
        var (_, sessions) = ServicesAt(Now);

        var laptop = await sessions.StartAsync(personId);
        var phone = await sessions.StartAsync(personId);

        await sessions.RevokeAllForPersonAsync(personId);

        Assert.False((await sessions.ValidateAsync(laptop)).Accepted);
        Assert.False((await sessions.ValidateAsync(phone)).Accepted);
    }

    [Fact]
    public async Task Revocation_takes_effect_on_the_very_next_request()
    {
        // The whole reason sessions are opaque rather than JWTs. With a JWT
        // this test could not pass without waiting for expiry.
        var personId = await db.AddPersonAsync($"immediate-{Guid.NewGuid():N}@example.com");
        var (_, sessions) = ServicesAt(Now);
        var token = await sessions.StartAsync(personId);

        Assert.True((await sessions.ValidateAsync(token)).Accepted);
        await sessions.RevokeAllForPersonAsync(personId);
        Assert.False((await sessions.ValidateAsync(token)).Accepted);
    }

    [Fact]
    public async Task An_expired_session_stops_validating()
    {
        var personId = await db.AddPersonAsync($"stale-{Guid.NewGuid():N}@example.com");
        var (_, issuer) = ServicesAt(Now);
        var token = await issuer.StartAsync(personId);

        var (_, later) = ServicesAt(Now.Add(SessionService.Lifetime).AddSeconds(1));

        Assert.Equal(TokenRejection.Expired, (await later.ValidateAsync(token)).Rejection);
    }

    [Fact]
    public async Task A_revoked_person_can_no_longer_be_issued_a_link()
    {
        var personId = await db.AddPersonAsync($"gone-{Guid.NewGuid():N}@example.com");
        var email = await EmailOf(personId);

        await using (var cmd = db.DataSource.CreateCommand(
            "UPDATE identity.people SET revoked_at = now() WHERE id = @id"))
        {
            cmd.Parameters.AddWithValue("id", personId);
            await cmd.ExecuteNonQueryAsync();
        }

        var (links, _) = ServicesAt(Now);
        Assert.Null(await links.IssueAsync(email));
    }

    [Fact]
    public async Task Email_lookup_ignores_case()
    {
        var personId = await db.AddPersonAsync($"MiXeD-{Guid.NewGuid():N}@Example.COM");
        var email = await EmailOf(personId);
        var (links, _) = ServicesAt(Now);

        Assert.NotNull(await links.IssueAsync(email.ToUpperInvariant()));
        Assert.NotNull(await links.IssueAsync(email.ToLowerInvariant()));
    }

    private async Task<string> EmailOf(Guid personId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT email FROM identity.people WHERE id = @id");
        cmd.Parameters.AddWithValue("id", personId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public class SecureTokenTests
{
    [Fact]
    public void Tokens_are_url_safe()
    {
        // These travel in magic links and cookies. A '+' or '/' surviving a
        // helpful email client is a login that silently fails.
        for (var i = 0; i < 200; i++)
        {
            var (raw, _) = SecureToken.Issue();
            Assert.DoesNotContain('+', raw);
            Assert.DoesNotContain('/', raw);
            Assert.DoesNotContain('=', raw);
        }
    }

    [Fact]
    public void Tokens_do_not_repeat()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 1_000; i++)
        {
            Assert.True(seen.Add(SecureToken.Issue().Raw));
        }
    }

    [Fact]
    public void The_raw_token_is_not_recoverable_from_its_hash()
    {
        var (raw, hash) = SecureToken.Issue();

        Assert.Equal(32, hash.Length);
        Assert.DoesNotContain(raw, Convert.ToBase64String(hash));
        Assert.Equal(hash, SecureToken.Hash(raw));
    }
}
