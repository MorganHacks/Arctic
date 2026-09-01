namespace MorganHacks.Applications.Domain;

/// <summary>
/// What the applicant is told, as opposed to what we record.
/// </summary>
/// <remarks>
/// Internal status is never shown directly. "Submitted" and "under review"
/// mean nothing different to an applicant, and the difference between them
/// tells them things we may not have decided to say yet.
/// <para>
/// This mapping is what lets reviewers work through the queue for a week while
/// every applicant still reads the same thing, and lets the announcement go
/// out when the team chooses rather than the moment a reviewer clicks.
/// </para>
/// <para>
/// The wording here is applicant-facing copy taken from
/// <c>morganhacks-applications-model.md</c>. It is in one place so it can be
/// changed in one place once the team has signed it off.
/// </para>
/// </remarks>
public static class ApplicantView
{
    /// <summary>
    /// Describes an application to the person who made it.
    /// </summary>
    /// <param name="status">The internal status.</param>
    /// <param name="decisionsAnnounced">
    /// Whether decisions have been released. Until this is set, a decided
    /// application reads exactly like an undecided one — which is the whole
    /// point of having a separate mapping.
    /// </param>
    /// <param name="rsvpDeadline">Shown to an accepted applicant, who needs the date.</param>
    /// <param name="eventStartsAt">Shown to a confirmed applicant.</param>
    public static string Describe(
        ApplicationStatus status,
        bool decisionsAnnounced = false,
        DateTimeOffset? rsvpDeadline = null,
        DateTimeOffset? eventStartsAt = null)
    {
        // Before the announcement, a decision reads as no decision. Confirmed,
        // declined and expired are not gated: reaching any of them means the
        // applicant was already told and acted, so hiding it now would be
        // showing them less than they already know.
        if (!decisionsAnnounced && status is ApplicationStatus.Accepted
            or ApplicationStatus.Rejected or ApplicationStatus.Waitlisted)
        {
            return "Application received";
        }

        return status switch
        {
            ApplicationStatus.Incomplete => "Application started",
            ApplicationStatus.Submitted or ApplicationStatus.UnderReview => "Application received",
            ApplicationStatus.Accepted => rsvpDeadline is { } by
                ? $"Accepted — confirm by {by:MMMM d}"
                : "Accepted",
            ApplicationStatus.Confirmed => eventStartsAt is { } on
                ? $"You're in. See you {on:MMMM d}"
                : "You're in",
            ApplicationStatus.Waitlisted => "Waitlisted",
            ApplicationStatus.Rejected => "Decision made",
            ApplicationStatus.Expired => "Confirmation deadline passed",
            ApplicationStatus.Declined or ApplicationStatus.Withdrawn => "Withdrawn",
            ApplicationStatus.CheckedIn => "Checked in",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }
}
