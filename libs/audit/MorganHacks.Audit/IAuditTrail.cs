namespace MorganHacks.Audit;

/// <summary>
/// Reads the permission audit trail.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no write method here, and that is deliberate.</b> If you came
/// looking for one, the trail is written by database triggers —
/// <c>0010_audit.sql</c> — inside whatever transaction made the change. An
/// <c>InsertAsync</c> on this interface would be a second way to write the
/// table that could be forgotten, could disagree with what actually happened,
/// and could be called without the change it claims to describe. The only
/// thing an application tells the trail is who is acting, and that goes
/// through <see cref="AuditContext"/>.
/// </para>
/// <para>
/// <b>There is no update or delete path either.</b> Not here, not in the
/// store, not anywhere: the entries table refuses both in the database, so a
/// connection is not enough to rewrite the record of what somebody did with
/// their connection. Retention, if it ever applies, is a migration that drops
/// that guard on purpose.
/// </para>
/// <para>
/// A library rather than a service, for the reason the README gives: a network
/// hop would let an audit write fail independently of the action it records,
/// and "the grant went through but we do not know who did it" is the single
/// outcome this whole thing exists to prevent.
/// </para>
/// </remarks>
public interface IAuditTrail
{
    /// <summary>
    /// The matching entries, newest first.
    /// </summary>
    /// <remarks>
    /// Newest first because every question asked of an audit trail starts with
    /// "what just happened". Oldest-first would mean paging to the end to see
    /// the change somebody is standing next to you asking about.
    /// </remarks>
    Task<IReadOnlyList<AuditEntry>> ReadAsync(AuditQuery query, CancellationToken ct = default);
}
