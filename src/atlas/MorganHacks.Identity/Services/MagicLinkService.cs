using MorganHacks.Identity.Domain;

namespace MorganHacks.Identity.Services;

/// <summary>
/// Passwordless login for hackers. No password is ever created, stored or
/// reset.
/// </summary>
public sealed class MagicLinkService(IIdentityStore store, TimeProvider clock)
{
    /// <summary>
    /// Short because the link is a bearer credential sitting in an inbox, and
    /// inboxes get forwarded, backed up and shared.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Issues a link for an address, if that address belongs to anyone.
    /// </summary>
    /// <returns>
    /// The raw token when one was issued, and <c>null</c> when the address is
    /// unknown.
    /// </returns>
    /// <remarks>
    /// The caller must respond identically either way. Returning "no account
    /// found" for one address and "check your inbox" for another builds an
    /// endpoint that tells anyone who asks exactly who applied to the
    /// hackathon.
    /// <para>
    /// This method deliberately does not throw for unknown addresses, so the
    /// only way to leak the difference is for a caller to go out of its way to
    /// branch on the result.
    /// </para>
    /// </remarks>
    public async Task<string?> IssueAsync(string email, CancellationToken ct = default)
    {
        var personId = await store.FindPersonIdByEmailAsync(email, ct);
        if (personId is null)
        {
            return null;
        }

        var (raw, hash) = SecureToken.Issue();
        await store.InsertMagicLinkAsync(
            personId.Value, hash, clock.GetUtcNow().Add(Lifetime), ct);

        return raw;
    }

    /// <summary>
    /// Spends a token. Single use: consumed on click rather than on expiry.
    /// </summary>
    public Task<TokenResult> ConsumeAsync(string rawToken, CancellationToken ct = default) =>
        store.ConsumeMagicLinkAsync(SecureToken.Hash(rawToken), clock.GetUtcNow(), ct);
}
