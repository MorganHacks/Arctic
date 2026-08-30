using MorganHacks.Identity.Domain;

namespace MorganHacks.Identity.Services;

/// <summary>
/// The persistence this module needs. Kept as a port so the state machine
/// above it can be tested without a database, and so the one operation that
/// genuinely must be atomic is a single named method rather than a read
/// followed by a write.
/// </summary>
public interface IIdentityStore
{
    Task InsertMagicLinkAsync(
        Guid personId, byte[] tokenHash, DateTimeOffset expiresAt, CancellationToken ct);

    /// <summary>
    /// Consumes a magic-link token if it is live, and returns who it belonged
    /// to.
    /// </summary>
    /// <remarks>
    /// Must be atomic. Implemented as a single conditional UPDATE rather than
    /// a SELECT then an UPDATE: two clicks arriving together — which happens
    /// for real, because mail clients and link scanners prefetch — would both
    /// pass a separate existence check and both mint a session from one token.
    /// </remarks>
    Task<TokenResult> ConsumeMagicLinkAsync(
        byte[] tokenHash, DateTimeOffset now, CancellationToken ct);

    Task InsertSessionAsync(
        Guid personId, byte[] tokenHash, DateTimeOffset expiresAt,
        string? userAgent, string? ip, CancellationToken ct);

    Task<TokenResult> ValidateSessionAsync(
        byte[] tokenHash, DateTimeOffset now, CancellationToken ct);

    Task RevokeSessionAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken ct);

    Task RevokeAllSessionsForPersonAsync(
        Guid personId, DateTimeOffset now, CancellationToken ct);

    Task<Guid?> FindPersonIdByEmailAsync(string email, CancellationToken ct);
}
