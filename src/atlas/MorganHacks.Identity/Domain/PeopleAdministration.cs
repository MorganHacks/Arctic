namespace MorganHacks.Identity.Domain;

/// <summary>Why an address could not be added to the organizer allowlist.</summary>
public enum AddOrganizerRejection
{
    /// <summary>
    /// The address is already an organizer.
    /// </summary>
    /// <remarks>
    /// Reported rather than treated as success. Two admins adding the same
    /// person is harmless, but so is saying so — and the alternative, a silent
    /// no-op, hides the case that actually matters: somebody typing an address
    /// that already belongs to a colleague and assuming they created a fresh
    /// account for it.
    /// </remarks>
    AlreadyAnOrganizer,

    /// <summary>
    /// The address belongs to an organizer whose access was revoked.
    /// </summary>
    /// <remarks>
    /// Told apart from <see cref="AlreadyAnOrganizer"/> because the two need
    /// opposite actions and look identical from the outside. Adding a revoked
    /// colleague back is the obvious thing to try, and it cannot work: the row
    /// already exists, so the insert matches nothing and the revocation stays
    /// where it was. Without this case the admin is told "already an
    /// organizer" about somebody who cannot sign in, and has no way to find
    /// out why from the console.
    /// </remarks>
    AlreadyAnOrganizerButRevoked,

    /// <summary>
    /// The address already has a hacker account.
    /// </summary>
    /// <remarks>
    /// Enforced by the unique index on <c>lower(email)</c>, and deliberate:
    /// an organizer account is never also an applicant account. An organizer
    /// who wants to test the hacker flow registers with a different address.
    /// </remarks>
    AddressIsAHackerAccount,
}

public readonly record struct AddOrganizerResult
{
    private AddOrganizerResult(Guid personId, AddOrganizerRejection? rejection)
    {
        PersonId = personId;
        Rejection = rejection;
    }

    public Guid PersonId { get; }
    public AddOrganizerRejection? Rejection { get; }
    public bool Accepted => Rejection is null;

    public static AddOrganizerResult Accept(Guid personId) => new(personId, null);
    public static AddOrganizerResult Reject(AddOrganizerRejection why) => new(Guid.Empty, why);

    /// <summary>
    /// Refused, naming the person already holding the address.
    /// </summary>
    /// <remarks>
    /// Only the revoked case carries an id, and only because the fix is a
    /// second request against that person. Telling an admin "restore them
    /// instead" without saying which row to restore leaves them searching a
    /// list for an address the console has just refused to show them.
    /// </remarks>
    public static AddOrganizerResult Reject(AddOrganizerRejection why, Guid personId) =>
        new(personId, why);
}

/// <summary>
/// What adding somebody to a team turned out to be.
/// </summary>
/// <remarks>
/// More than a bool because one caller needs to know whether this was the
/// moment the person's access became real. Joining a first team is when an
/// organizer goes from "on the allowlist, can see nothing" to "can do the job"
/// — the only point at which telling them so is worth an email.
/// <para>
/// <see cref="FirstTeam"/> is decided against the memberships that existed
/// before the insert, in the same statement, so re-adding somebody to a team
/// they are already on is not a first and cannot mail them twice.
/// </para>
/// </remarks>
/// <param name="Matched">
/// False when there is no such person or no such team, which is the only
/// failure this write has.
/// </param>
/// <param name="Email">
/// Theirs, carried back so a caller that is about to write to them does not
/// need a second query and cannot race a change of address. Empty when
/// nothing matched.
/// </param>
/// <param name="Active">
/// False when the person is revoked. Adding a revoked person to a team is
/// legitimate — it is how somebody is set up before being restored — but they
/// cannot sign in, so nothing should tell them they can.
/// </param>
public readonly record struct JoinTeamResult(
    bool Matched, bool FirstTeam, string Email, bool Active)
{
    public static readonly JoinTeamResult NoSuchThing = new(false, false, "", false);
}

/// <summary>
/// One person, as the admin detail screen needs them.
/// </summary>
/// <remarks>
/// Wider than <see cref="Services.PersonSummary"/> because this is the screen
/// where somebody works out why a person can or cannot do a thing, and that
/// answer is unreadable without the expiry dates. Still not the whole row: the
/// Google subject id stays inside the module, because nothing on a screen is
/// improved by it.
/// </remarks>
public sealed record PersonDetail(
    Guid Id,
    string Kind,
    string Email,
    DateTimeOffset? RevokedAt,
    IReadOnlyList<TeamMembership> Teams,
    IReadOnlyList<PermissionGrant> Grants,
    bool Linked = false,
    string? FullName = null,
    string? AvatarUrl = null)
{
    public bool Revoked => RevokedAt is not null;
}

/// <summary>A team and the baseline it confers, named for a human.</summary>
/// <remarks>
/// <see cref="TeamBaseline"/> carries the same permissions without the display
/// name, because permission resolution has no use for one. The admin screens
/// do: "Registration" is what an organizer recognises, not "registration".
/// </remarks>
public sealed record TeamSummary(
    string Slug,
    string Name,
    IReadOnlySet<Permission> Permissions);
