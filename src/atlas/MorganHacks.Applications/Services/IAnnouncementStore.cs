namespace MorganHacks.Applications.Services;

/// <summary>
/// One short notice posted to everybody at an event.
/// </summary>
/// <remarks>
/// The organizers' shape, which is why the retraction fields are on it. The
/// applicant never sees a retracted announcement at all — see
/// <see cref="IApplicantPortalStore.AnnouncementsForPersonAsync"/> — so there
/// is nothing for them to be told about one.
/// <para>
/// <see cref="Body"/> is the words an organizer typed, and it is the one field
/// in this system that is written by an organizer and read by every applicant.
/// Nothing renders merge fields into it and nothing personalises it, because
/// there is nobody to personalise it for: one row is shown identically to
/// everybody at the event. That is the whole reason it is safe for the portal
/// to hand it out without a per-person check.
/// </para>
/// </remarks>
public sealed record Announcement(
    Guid Id,
    Guid EventId,
    string Body,
    DateTimeOffset PostedAt,
    Guid PostedBy,
    DateTimeOffset? RetractedAt,
    Guid? RetractedBy);

/// <summary>
/// Posting a notice, listing them, and taking one back down.
/// </summary>
/// <remarks>
/// The organizers' surface. Everything here is scoped by an event id the
/// caller names, which is exactly what makes it the organizers' surface and
/// not the applicants' — the applicant read lives on
/// <see cref="IApplicantPortalStore"/>, where every query is narrowed by the
/// session's person id and nothing takes an id from a request. The split is
/// the same one <see cref="IApplicationStore"/> and
/// <see cref="IApplicantPortalStore"/> already draw, for the same reason: the
/// two have opposite defaults and merging them would put a method that can be
/// pointed anywhere on the interface the portal calls.
/// <para>
/// There is no update method, and there should never be one. A posted notice
/// is not edited — see <c>0024</c> for the argument, which is that people have
/// already read it and there is no way to tell them it changed. What replaces
/// an edit is <see cref="RetractAsync"/> followed by a second
/// <see cref="PostAsync"/>, which leaves both in the feed in the order they
/// happened.
/// </para>
/// <para>
/// Nothing here deletes either, for the reason nothing else in this system
/// does: a deleted row makes "we never said that" and "we said it and took it
/// back" the same database state.
/// </para>
/// </remarks>
public interface IAnnouncementStore
{
    /// <summary>
    /// Posts one, and records who posted it.
    /// </summary>
    /// <remarks>
    /// <paramref name="postedBy"/> is not nullable because the column is not.
    /// Every announcement is written by a session holding
    /// <c>announcements.post</c>, so there is no honest unknown author to
    /// allow for.
    /// </remarks>
    /// <exception cref="Npgsql.PostgresException">
    /// SQLSTATE 23503 when the event id names nothing. Left to the caller
    /// because the answer is a sentence about that event rather than a fault.
    /// </exception>
    Task<Announcement> PostAsync(
        Guid eventId, string body, Guid postedBy, CancellationToken ct = default);

    /// <summary>
    /// Every announcement for an event, retracted ones included, newest first.
    /// </summary>
    /// <remarks>
    /// The organizers' list, which is why it does not filter. Somebody who
    /// took a notice down at 2pm needs to see that they did; a list that
    /// simply dropped it would look identical to one where the retraction
    /// never landed.
    /// </remarks>
    Task<IReadOnlyList<Announcement>> ForEventAsync(
        Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Takes one down, or returns null when there is no such id.
    /// </summary>
    /// <remarks>
    /// Idempotent. Retracting an already-retracted notice returns the row as
    /// it stands, with the first retractor still named on it, rather than
    /// failing: during the hour this gets used, two organizers both reaching
    /// for the same mistake is the normal case and neither of them should be
    /// shown an error for agreeing.
    /// </remarks>
    Task<Announcement?> RetractAsync(
        Guid id, Guid retractedBy, CancellationToken ct = default);
}
