using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MorganHacks.Identity.Data;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Which door each kind of person comes through, and what happens the moment
/// their access is taken away.
/// </summary>
/// <remarks>
/// Both of these were real holes rather than hypotheticals: organizers could
/// sign in by email and skip Google entirely, and revoking somebody left every
/// session and pending link they held still working.
/// </remarks>
public class RevocationTests(IdentityDatabase db)
    : IClassFixture<IdentityDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;
    private PostgresIdentityStore Store => new(db.DataSource);
    private MagicLinkService Links => new(Store, TimeProvider.System);
    private SessionService Sessions => new(Store, TimeProvider.System);
    private static string Unique(string p) => $"{p}-{Guid.NewGuid():N}@example.com";

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

    [Fact]
    public async Task An_organizer_cannot_get_a_magic_link()
    {
        // Organizers sign in through Google so that access is tied to an
        // allowlisted account and a subject id bound on first login. A magic
        // link would hand out the same session on nothing but inbox access,
        // and the super admin holds every permission there is.
        var email = Unique("organizer");
        await db.AddPersonAsync(email, "organizer");

        Assert.Null(await Links.IssueAsync(email));
    }

    [Fact]
    public async Task A_hacker_can_still_get_a_magic_link()
    {
        // The other half of the same rule: closing the organizer door must not
        // close the one the whole hacker flow depends on.
        var email = Unique("hacker");
        await db.AddPersonAsync(email);

        Assert.NotNull(await Links.IssueAsync(email));
    }

    [Fact]
    public async Task An_organizer_address_is_answered_like_any_other()
    {
        // Refusing organizers must not make the endpoint say so. A different
        // status or body for organizer addresses turns it into a way to find
        // out who runs the event.
        var organizer = Unique("organizer");
        await db.AddPersonAsync(organizer, "organizer");
        var hacker = Unique("hacker");
        await db.AddPersonAsync(hacker);

        var client = _app.CreateClient();
        var a = await client.PostAsJsonAsync("/auth/magic-link", new { email = organizer });
        var b = await client.PostAsJsonAsync("/auth/magic-link", new { email = hacker });

        Assert.Equal(HttpStatusCode.Accepted, a.StatusCode);
        Assert.Equal(a.StatusCode, b.StatusCode);
        Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Revoking_a_person_ends_the_session_they_already_hold()
    {
        // The point of opaque sessions. If this fails they keep their access
        // until the token expires, which is up to fourteen days of reading
        // applicant PII after being removed.
        var personId = await db.AddPersonAsync(Unique("hacker"));
        var token = await Sessions.StartAsync(personId);

        Assert.True((await Sessions.ValidateAsync(token)).Accepted);

        await db.RevokeAsync(personId);

        var result = await Sessions.ValidateAsync(token);
        Assert.False(result.Accepted);
        Assert.Equal(TokenRejection.Revoked, result.Rejection);
    }

    [Fact]
    public async Task Revoking_a_person_kills_the_link_already_in_their_inbox()
    {
        // A link issued a minute before someone was removed is still sitting
        // in their mail. Revocation has to reach it.
        var email = Unique("hacker");
        var personId = await db.AddPersonAsync(email);
        var token = await Links.IssueAsync(email);

        await db.RevokeAsync(personId);

        var result = await Links.ConsumeAsync(token!);
        Assert.False(result.Accepted);
        Assert.Equal(TokenRejection.Revoked, result.Rejection);
    }

    [Fact]
    public async Task A_revoked_person_cannot_be_issued_a_new_link_either()
    {
        var email = Unique("hacker");
        var personId = await db.AddPersonAsync(email);
        await db.RevokeAsync(personId);

        Assert.Null(await Links.IssueAsync(email));
    }
}
