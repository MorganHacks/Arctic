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
    IReadOnlyList<PermissionGrant> Grants)
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
