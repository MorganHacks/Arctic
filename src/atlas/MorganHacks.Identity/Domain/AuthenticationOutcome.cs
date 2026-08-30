namespace MorganHacks.Identity.Domain;

/// <summary>Why a presented token did not produce a session.</summary>
public enum TokenRejection
{
    /// <summary>No row matched the presented token's hash.</summary>
    NotFound,

    /// <summary>Past its expiry.</summary>
    Expired,

    /// <summary>Already used. Magic links are single use, consumed on click.</summary>
    AlreadyConsumed,

    /// <summary>Revoked before it expired.</summary>
    Revoked,
}

/// <summary>The result of presenting a magic-link token or a session token.</summary>
public readonly record struct TokenResult
{
    private TokenResult(Guid personId, TokenRejection? rejection)
    {
        PersonId = personId;
        Rejection = rejection;
    }

    public Guid PersonId { get; }
    public TokenRejection? Rejection { get; }
    public bool Accepted => Rejection is null;

    public static TokenResult Accept(Guid personId) => new(personId, null);
    public static TokenResult Reject(TokenRejection why) => new(Guid.Empty, why);
}
