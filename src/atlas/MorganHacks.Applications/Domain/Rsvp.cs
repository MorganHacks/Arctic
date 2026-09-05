namespace MorganHacks.Applications.Domain;

/// <summary>What an applicant is answering when they answer.</summary>
public enum RsvpAnswer
{
    /// <summary>They are coming.</summary>
    Confirm,

    /// <summary>They are not, and the spot goes back.</summary>
    Decline,
}

/// <summary>
/// When an applicant may answer for their own spot, and what to tell them when
/// they may not.
/// </summary>
/// <remarks>
/// A rule rather than a screen state, the same shape as
/// <see cref="ProfileEditing"/> and for the same reason: the portal shows the
/// buttons from this, the endpoint refuses the write from this, and a check
/// only one of them makes is decoration. A hidden button is not a gate.
/// <para>
/// The lifecycle table is upstream of everything here.
/// <see cref="StatusTransition"/> already says an application may go from
/// <c>accepted</c> to <c>confirmed</c> or <c>declined</c> and from nowhere
/// else, and that <c>declined</c> goes nowhere at all. This file adds the two
/// things the table cannot know: that the person doing it is the applicant,
/// and that there is a date past which the offer is not theirs any more.
/// </para>
/// </remarks>
public static class Rsvp
{
    /// <summary>
    /// Whether the deadline has gone.
    /// </summary>
    /// <remarks>
    /// A null deadline has not passed, and that is the decision worth writing
    /// down: <c>rsvp_deadline</c> is nullable with no default, so "no deadline
    /// set" is the ordinary state of the column for most of the year and for
    /// every application nobody has decided yet. Reading null as "closed"
    /// would mean an accepted applicant cannot answer until an organizer
    /// remembers to fill in a date — a spot they were offered and cannot take,
    /// caused by a field nobody knew was required. The acceptance is the offer;
    /// the deadline is an optional limit on it.
    /// </remarks>
    public static bool DeadlineHasPassed(DateTimeOffset? deadline, DateTimeOffset now) =>
        deadline is { } by && now > by;

    /// <summary>
    /// Whether this applicant may confirm or decline right now.
    /// </summary>
    /// <param name="status">The internal status.</param>
    /// <param name="decisionsAnnounced">
    /// Whether the result has been released. Gated here as well as in
    /// <see cref="ApplicantView"/>, because a confirm button is a decision
    /// shown on a screen: offering it to somebody who is being told
    /// "Application received" announces their acceptance through the one
    /// control they can see working.
    /// </param>
    /// <param name="deadline">The RSVP deadline, or null when none is set.</param>
    /// <param name="now">The moment being judged, so a test can pick one.</param>
    public static bool IsOpen(
        ApplicationStatus status,
        bool decisionsAnnounced,
        DateTimeOffset? deadline,
        DateTimeOffset now) =>
        status is ApplicationStatus.Accepted
        && decisionsAnnounced
        && !DeadlineHasPassed(deadline, now);

    /// <summary>
    /// Why they cannot, or null while they can.
    /// </summary>
    /// <remarks>
    /// A sentence rather than a bare refusal, matching
    /// <see cref="ProfileEditing.WhyClosed"/>. Every one of these is said to
    /// somebody who just pressed a button and needs to know whether to press
    /// it again, write to us, or stop.
    /// <para>
    /// The wording never names the internal status, and an undecided-looking
    /// application and an accepted-but-unannounced one share
    /// <see cref="Nothing"/> word for word. An applicant comparing screens with
    /// a friend is the threat the whole mapping exists for, and a refusal
    /// message is a screen like any other.
    /// </para>
    /// </remarks>
    public static string? WhyClosed(
        ApplicationStatus status,
        bool decisionsAnnounced,
        DateTimeOffset? deadline,
        DateTimeOffset now)
    {
        if (IsOpen(status, decisionsAnnounced, deadline, now))
        {
            return null;
        }

        // Before the announcement a decided application must read exactly like
        // an undecided one, which includes why it has nothing to answer.
        if (!decisionsAnnounced && status is ApplicationStatus.Accepted
            or ApplicationStatus.Rejected or ApplicationStatus.Waitlisted)
        {
            return Nothing;
        }

        // Said in the applicant's words rather than the column's, and not only
        // for tone: "confirmed" and "declined" are the stored spellings, and a
        // sentence containing one is the internal status reaching a screen by
        // the back door. PortalTests checks every wire spelling against every
        // byte this route sends.
        return status switch
        {
            ApplicationStatus.Confirmed or ApplicationStatus.CheckedIn =>
                "You have already told us you are coming.",

            // Terminal in StatusTransition, so this is the whole answer: there
            // is no route back and the sentence should not imply one this
            // portal can take.
            ApplicationStatus.Declined =>
                "You have already told us you cannot make it. Email us if that "
                + "was a mistake.",

            // Expired is the deadline having passed and the hourly job having
            // noticed. Accepted only reaches here when it has passed and the
            // job has not run yet, so both get the same sentence — the
            // applicant is in the same position either way.
            ApplicationStatus.Expired or ApplicationStatus.Accepted =>
                "The window to confirm has closed. Email us if you still want "
                + "to come.",

            _ => Nothing,
        };
    }

    /// <summary>
    /// Said to everybody who has nothing to answer, decided or not.
    /// </summary>
    /// <remarks>
    /// One constant rather than the same sentence written twice, for the
    /// reason <see cref="ApplicantView"/> keeps one: the two cases it covers
    /// have to stay identical to the character.
    /// </remarks>
    private const string Nothing = "There is nothing to confirm right now.";

    /// <summary>The status an answer moves the application to.</summary>
    public static ApplicationStatus Target(RsvpAnswer answer) => answer switch
    {
        RsvpAnswer.Confirm => ApplicationStatus.Confirmed,
        RsvpAnswer.Decline => ApplicationStatus.Declined,
        _ => throw new ArgumentOutOfRangeException(nameof(answer), answer, null),
    };

    /// <summary>
    /// Reads the word an applicant sent.
    /// </summary>
    /// <remarks>
    /// Verbs, not statuses. The portal never says <c>confirmed</c> or
    /// <c>declined</c> in either direction — an API that accepts the stored
    /// spelling is one where the stored spelling ends up in a fetch call on a
    /// screen, and from there in a bug report.
    /// </remarks>
    public static bool TryParseAnswer(string? submitted, out RsvpAnswer answer)
    {
        switch (submitted?.Trim().ToLowerInvariant())
        {
            case "confirm":
                answer = RsvpAnswer.Confirm;
                return true;
            case "decline":
                answer = RsvpAnswer.Decline;
                return true;
            default:
                answer = default;
                return false;
        }
    }
}
