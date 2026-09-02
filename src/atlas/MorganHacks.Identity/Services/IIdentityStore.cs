using MorganHacks.Identity.Domain;

namespace MorganHacks.Identity.Services;

/// <summary>One row on the admin people screen.</summary>
/// <remarks>
/// Deliberately not the whole person. A screen that lists everybody does not
/// need their Google subject id or when they last signed in, and the less of
/// that leaves the module the less there is to leak.
/// </remarks>
public sealed record PersonSummary(
    Guid Id,
    string Kind,
    string Email,
    bool Revoked,
    IReadOnlyList<string> Teams);

/// <summary>
/// The persistence this module needs. Kept as a port so the state machine
/// above it can be tested without a database, and so the one operation that
/// genuinely must be atomic is a single named method rather than a read
/// followed by a write.
/// </summary>
/// <remarks>
/// Every method that changes what somebody may do takes an <c>actorId</c>, and
/// it is not nullable. The trail records null where nobody was behind a change
/// — a seed, an import, a fix run by hand — and that is a real and useful
/// answer, which is exactly why these methods must not be able to produce it.
/// Every caller of them is an endpoint the permission gate has already
/// resolved a person for. A nullable parameter would let one of them pass null
/// by accident and have an admin's action recorded as anonymous, and there is
/// no way to tell that apart afterwards from the honest kind.
/// <para>
/// None of them takes an audit row, or returns one. The database writes the
/// trail inside the same transaction as the change; see <c>libs/audit</c>.
/// </para>
/// </remarks>
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

    Task<Guid?> FindHackerIdByEmailAsync(string email, CancellationToken ct);

    /// <summary>
    /// Everyone with an account, for the admin people screen.
    /// </summary>
    /// <remarks>
    /// Unpaged on purpose. This lists people who can sign in — organizers and
    /// registered hackers — not applicants, so it is tens of rows rather than
    /// hundreds. Paging it now would be machinery guarding against a number
    /// that is not coming.
    /// </remarks>
    Task<IReadOnlyList<PersonSummary>> ListPeopleAsync(CancellationToken ct);

    /// <summary>
    /// Resolves a verified Google identity to an organizer, binding the Google
    /// subject id on first sign-in.
    /// </summary>
    /// <remarks>
    /// Google says who someone is. It does not say they are allowed in — those
    /// are different questions, and conflating them makes every Gmail account
    /// an organizer.
    /// <para>
    /// Matching prefers the subject id over the address, so an organizer who
    /// changes their Google email is not locked out. Binding on first
    /// successful sign-in means nobody can claim an allowlisted address they
    /// do not actually control.
    /// </para>
    /// </remarks>
    Task<OrganizerResult> ResolveOrganizerAsync(
        GoogleIdentity identity, CancellationToken ct);

    /// <summary>
    /// Everything needed to work out what one person may do: their team
    /// memberships, their individual grants, and the baseline each team
    /// confers.
    /// </summary>
    /// <remarks>
    /// Returned as raw rows rather than a decision, so the additive-union rule
    /// lives in one testable place instead of in SQL.
    /// </remarks>
    Task<(IReadOnlyList<TeamMembership> Memberships,
          IReadOnlyList<PermissionGrant> Grants,
          IReadOnlyList<TeamBaseline> Baselines)>
        GetPermissionContextAsync(Guid personId, CancellationToken ct);

    /// <summary>One person and every membership and grant attached to them.</summary>
    /// <remarks>
    /// Null rather than an exception for an id that does not exist, because
    /// the caller is a URL somebody can edit and a 404 is the right answer to
    /// a made-up one.
    /// </remarks>
    Task<PersonDetail?> FindPersonAsync(Guid personId, CancellationToken ct);

    /// <summary>Every team and the baseline it confers.</summary>
    Task<IReadOnlyList<TeamSummary>> ListTeamsAsync(CancellationToken ct);

    /// <summary>
    /// Puts an address on the organizer allowlist.
    /// </summary>
    /// <remarks>
    /// The row is the allowlist — there is no separate table — so this is the
    /// whole of "add an organizer". No Google subject id is set: binding one
    /// here would mean trusting that whoever typed the address also controls
    /// the account, which is exactly what first-sign-in binding exists to
    /// avoid.
    /// </remarks>
    Task<AddOrganizerResult> AddOrganizerAsync(
        string email, Guid actorId, CancellationToken ct);

    /// <summary>
    /// Adds someone to a team, or changes the expiry if they are already on it.
    /// </summary>
    /// <remarks>
    /// Upserts rather than failing on a duplicate, because "add them until the
    /// Sunday after the event" and "actually, make that the Monday" are the
    /// same intent expressed twice, and an admin should not have to remove a
    /// membership in order to shorten it.
    /// </remarks>
    /// <returns>False when no such person or no such team.</returns>
    Task<bool> AddToTeamAsync(
        Guid personId, string teamSlug, DateTimeOffset? expiresAt,
        Guid actorId, CancellationToken ct);

    /// <returns>False when the person was not on that team to begin with.</returns>
    Task<bool> RemoveFromTeamAsync(
        Guid personId, string teamSlug, Guid actorId, CancellationToken ct);

    /// <summary>
    /// Grants one permission to one person directly, or changes its expiry.
    /// </summary>
    /// <remarks>
    /// Additive only, like everything else here. There is no counterpart that
    /// denies a permission a team confers: subtractive overrides make
    /// effective permissions impossible to reason about, and the answer to
    /// "they should not have this" is to take them off the team.
    /// </remarks>
    /// <returns>False when no such person.</returns>
    Task<bool> GrantAsync(
        Guid personId, Permission permission, DateTimeOffset? expiresAt,
        Guid grantedBy, CancellationToken ct);

    /// <returns>False when the person did not hold that grant.</returns>
    Task<bool> RevokeGrantAsync(
        Guid personId, Permission permission, Guid actorId, CancellationToken ct);

    /// <summary>
    /// Takes someone off the allowlist and ends every session they hold, in
    /// one transaction.
    /// </summary>
    /// <remarks>
    /// One transaction because the two halves are one decision. Setting
    /// <c>revoked_at</c> alone stops new sign-ins and leaves an open laptop
    /// working; cutting sessions alone lets the next sign-in restore them. A
    /// crash between two separate writes would leave exactly one of those
    /// states, and it is not knowable in advance which.
    /// <para>
    /// Sessions are cut even when the person was already revoked, so a second
    /// attempt after a partial failure finishes the job rather than reporting
    /// there was nothing to do.
    /// </para>
    /// </remarks>
    /// <returns>False when no such person.</returns>
    Task<bool> RevokePersonAsync(
        Guid personId, DateTimeOffset now, Guid actorId, CancellationToken ct);
}
