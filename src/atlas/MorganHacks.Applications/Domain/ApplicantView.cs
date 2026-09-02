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

    /// <summary>
    /// What the applicant should do next, or what they are waiting for.
    /// </summary>
    /// <remarks>
    /// Next to <see cref="Describe"/> rather than in the portal, for the same
    /// reason: this is applicant-facing copy the team has to be able to change
    /// in one place, and a status line that says "Application received" beside
    /// a paragraph written somewhere else is how the two come to disagree.
    /// <para>
    /// The announcement gate is repeated here rather than inferred. A sentence
    /// telling somebody to watch for a decision, shown only to those already
    /// decided, would leak the decision through its own helpfulness.
    /// </para>
    /// <para>
    /// Every line here is a draft pending sign-off. Nothing in it promises a
    /// date we have not published.
    /// </para>
    /// </remarks>
    public static string NextStep(
        ApplicationStatus status,
        bool decisionsAnnounced = false,
        DateTimeOffset? rsvpDeadline = null,
        DateTimeOffset? eventStartsAt = null)
    {
        if (!decisionsAnnounced && status is ApplicationStatus.Accepted
            or ApplicationStatus.Rejected or ApplicationStatus.Waitlisted)
        {
            return Waiting;
        }

        return status switch
        {
            ApplicationStatus.Incomplete =>
                "Finish your application to be considered. You can come back to "
                + "it as often as you like until you submit.",
            ApplicationStatus.Submitted or ApplicationStatus.UnderReview => Waiting,
            ApplicationStatus.Accepted => rsvpDeadline is { } by
                ? $"You have a spot. Confirm it by {by:MMMM d} or it goes to "
                  + "somebody on the waitlist."
                : "You have a spot. We will email you how to confirm it.",
            ApplicationStatus.Confirmed => eventStartsAt is { } on
                ? $"Nothing to do. We will email you the details before {on:MMMM d}."
                : "Nothing to do. We will email you the details closer to the event.",
            ApplicationStatus.Waitlisted =>
                "Keep an eye on your email. Spots open up as people drop out, "
                + "and we work down the list in order.",
            ApplicationStatus.Rejected =>
                "We could not offer you a spot this year. Applying again next "
                + "year does not count against you.",
            ApplicationStatus.Expired =>
                "The window to confirm has closed. Email us if you still want "
                + "to come — we would rather hear from you than not.",
            ApplicationStatus.Declined or ApplicationStatus.Withdrawn =>
                "Your application is closed at your request. Email us if that "
                + "was a mistake.",
            ApplicationStatus.CheckedIn => "You are checked in. Enjoy the event.",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }

    /// <summary>
    /// Said to everybody whose application is in, decided or not.
    /// </summary>
    /// <remarks>
    /// One constant rather than the same sentence written twice, because the
    /// two cases it covers must stay word for word identical: an applicant
    /// comparing screens with a friend is the threat model the whole mapping
    /// exists for.
    /// </remarks>
    private const string Waiting =
        "Nothing to do. We are reading applications and will email everybody "
        + "on the same day.";
}
