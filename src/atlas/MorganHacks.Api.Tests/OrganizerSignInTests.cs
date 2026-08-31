using MorganHacks.Identity.Data;
using MorganHacks.Identity.Domain;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The rules that decide whether a verified Google identity gets in.
/// </summary>
/// <remarks>
/// Google authenticates; these rules authorise. Everything here assumes the
/// token was already verified, because a token that fails verification never
/// reaches this code.
/// </remarks>
public class OrganizerSignInTests(IdentityDatabase db) : IClassFixture<IdentityDatabase>
{
    private PostgresIdentityStore Store => new(db.DataSource);
    private static string Unique(string p) => $"{p}-{Guid.NewGuid():N}@example.com";
    private static string Sub() => $"sub-{Guid.NewGuid():N}";

    [Fact]
    public async Task An_address_nobody_allowlisted_is_rejected()
    {
        // The whole point. Google will happily authenticate any Gmail account;
        // it does not follow that they organise our hackathon.
        var result = await Store.ResolveOrganizerAsync(
            new GoogleIdentity(Sub(), Unique("stranger")), default);

        Assert.Equal(OrganizerRejection.NotAllowlisted, result.Rejection);
    }

    [Fact]
    public async Task A_hacker_account_cannot_sign_in_as_an_organizer()
    {
        // Organizer accounts and hacker accounts are deliberately separate.
        var email = Unique("hacker");
        await db.AddPersonAsync(email, "hacker");

        var result = await Store.ResolveOrganizerAsync(new GoogleIdentity(Sub(), email), default);

        Assert.Equal(OrganizerRejection.NotAllowlisted, result.Rejection);
    }

    [Fact]
    public async Task First_sign_in_binds_the_google_subject()
    {
        var email = Unique("organizer");
        var personId = await db.AddPersonAsync(email, "organizer");
        var sub = Sub();

        var result = await Store.ResolveOrganizerAsync(new GoogleIdentity(sub, email), default);

        Assert.True(result.Accepted);
        Assert.Equal(personId, result.PersonId);
        Assert.Equal(sub, await db.GoogleSubOf(personId));
    }

    [Fact]
    public async Task A_changed_google_email_still_signs_in()
    {
        // Matching on the subject id rather than the address is what stops an
        // organizer being locked out when they change their Google email.
        var email = Unique("mover");
        var personId = await db.AddPersonAsync(email, "organizer");
        var sub = Sub();
        await Store.ResolveOrganizerAsync(new GoogleIdentity(sub, email), default);

        var later = await Store.ResolveOrganizerAsync(
            new GoogleIdentity(sub, Unique("brand-new-address")), default);

        Assert.True(later.Accepted);
        Assert.Equal(personId, later.PersonId);
    }

    [Fact]
    public async Task Nobody_can_claim_an_address_already_bound_to_someone_else()
    {
        // Somebody who controls an allowlisted-looking address cannot take it
        // over once the real owner has signed in.
        var email = Unique("taken");
        await db.AddPersonAsync(email, "organizer");
        await Store.ResolveOrganizerAsync(new GoogleIdentity(Sub(), email), default);

        var impostor = await Store.ResolveOrganizerAsync(
            new GoogleIdentity(Sub(), email), default);

        Assert.Equal(OrganizerRejection.BoundToAnotherAccount, impostor.Rejection);
    }

    [Fact]
    public async Task Two_simultaneous_first_sign_ins_bind_only_once()
    {
        // The bind is a conditional UPDATE, so of several racing first logins
        // exactly one may claim the row.
        var email = Unique("race");
        await db.AddPersonAsync(email, "organizer");

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ =>
                Store.ResolveOrganizerAsync(new GoogleIdentity(Sub(), email), default)));

        Assert.Equal(1, attempts.Count(a => a.Accepted));
    }

    [Fact]
    public async Task A_revoked_organizer_is_refused_even_with_a_valid_google_account()
    {
        // Removing someone means removing them from the allowlist. It has to
        // hold even though Google still authenticates them perfectly well.
        var email = Unique("gone");
        var personId = await db.AddPersonAsync(email, "organizer");
        var sub = Sub();
        await Store.ResolveOrganizerAsync(new GoogleIdentity(sub, email), default);
        await db.RevokeAsync(personId);

        var result = await Store.ResolveOrganizerAsync(new GoogleIdentity(sub, email), default);

        Assert.Equal(OrganizerRejection.Revoked, result.Rejection);
    }
}
