using MorganHacks.Identity.Domain;

namespace MorganHacks.Identity.Services;

/// <summary>
/// Sessions, as opaque references to a database row.
/// </summary>
/// <remarks>
/// Deliberately not JWTs. A JWT is valid until it expires and cannot be taken
/// back: revoke someone at 2pm against a token good until 3pm and they keep an
/// hour of access to applicant PII. Revoking one of these is a database write
/// that takes effect on the very next request.
/// <para>
/// The cost is a lookup per request, which at a few hundred applicants is
/// nothing next to being able to actually remove someone.
/// </para>
/// </remarks>
public sealed class SessionService(IIdentityStore store, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    /// <summary>
    /// Starts a session and returns the raw token, which is the only time it
    /// exists outside the holder's cookie.
    /// </summary>
    public async Task<string> StartAsync(
        Guid personId,
        string? userAgent = null,
        string? ip = null,
        CancellationToken ct = default)
    {
        var (raw, hash) = SecureToken.Issue();
        await store.InsertSessionAsync(
            personId, hash, clock.GetUtcNow().Add(Lifetime), userAgent, ip, ct);

        return raw;
    }

    public Task<TokenResult> ValidateAsync(string rawToken, CancellationToken ct = default) =>
        store.ValidateSessionAsync(SecureToken.Hash(rawToken), clock.GetUtcNow(), ct);

    public Task RevokeAsync(string rawToken, CancellationToken ct = default) =>
        store.RevokeSessionAsync(SecureToken.Hash(rawToken), clock.GetUtcNow(), ct);

    /// <summary>
    /// Ends every session a person holds. This is what "remove their access"
    /// means in practice, alongside taking them off the allowlist.
    /// </summary>
    public Task RevokeAllForPersonAsync(Guid personId, CancellationToken ct = default) =>
        store.RevokeAllSessionsForPersonAsync(personId, clock.GetUtcNow(), ct);
}
