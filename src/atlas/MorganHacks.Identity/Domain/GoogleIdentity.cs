namespace MorganHacks.Identity.Domain;

/// <summary>A verified Google identity. Only ever produced after signature and issuer checks.</summary>
public sealed record GoogleIdentity(string Subject, string Email);

/// <summary>Why an organizer sign-in did not succeed.</summary>
public enum OrganizerRejection
{
    /// <summary>The address is not on the allowlist. Google said who they are; we decide if they may in.</summary>
    NotAllowlisted,

    /// <summary>Their access was revoked.</summary>
    Revoked,

    /// <summary>
    /// The address is allowlisted but already bound to a different Google
    /// account. Rebinding is a deliberate admin action, never automatic.
    /// </summary>
    BoundToAnotherAccount,
}

public readonly record struct OrganizerResult
{
    private OrganizerResult(Guid personId, OrganizerRejection? rejection)
    {
        PersonId = personId;
        Rejection = rejection;
    }

    public Guid PersonId { get; }
    public OrganizerRejection? Rejection { get; }
    public bool Accepted => Rejection is null;

    public static OrganizerResult Accept(Guid personId) => new(personId, null);
    public static OrganizerResult Reject(OrganizerRejection why) => new(Guid.Empty, why);
}
