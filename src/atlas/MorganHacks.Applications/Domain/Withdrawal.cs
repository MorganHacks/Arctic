namespace MorganHacks.Applications.Domain;

/// <summary>
/// When an applicant may close their own application, and what to tell them
/// when they may not.
/// </summary>
/// <remarks>
/// A rule rather than a screen state, the same shape as <see cref="Rsvp"/> and
/// <see cref="ProfileEditing"/> and for the same reason: the portal shows the
/// button from this, the endpoint refuses the write from this, and a check
/// only one of them makes is decoration.
/// <para>
/// This exists because declining is not the same thing. An RSVP is answered by
/// somebody who has been offered a spot and is deciding about it; a withdrawal
/// is said by anybody at any point before the door — the applicant who took a
/// job for that weekend in March, the one still waiting on a decision in
/// April, the one who confirmed and then broke an ankle. Without it, the only
/// people who can tell us they are not coming are the ones we already accepted,
/// and everybody else is counted, catered for and mailed until the day.
/// </para>
/// <para>
/// <see cref="StatusTransition"/> is upstream of every line here and this file
/// adds no second list: <see cref="IsOpen"/> asks the table itself. What it
/// adds is the one thing the table cannot know — that the person doing it is
/// the applicant, reading a screen that is not allowed to tell them their
/// decision yet.
/// </para>
/// </remarks>
public static class Withdrawal
{
    /// <summary>
    /// Whether this applicant may withdraw right now.
    /// </summary>
    /// <param name="status">The internal status.</param>
    /// <param name="decisionsAnnounced">
    /// Whether the result has been released. It matters here for a reason that
    /// is not obvious: the lifecycle allows an accepted or waitlisted
    /// application to be withdrawn and allows a rejected one to go nowhere at
    /// all, so a portal that asked the table alone would put a working button
    /// in front of two of the three decided applicants and refuse the third.
    /// Before the announcement that button is the decision — the rejected
    /// applicant learns their answer by pressing it, days before the team
    /// meant to say so, and they learn it from a screen that is still telling
    /// them "Application received".
    /// <para>
    /// So the three decided states are closed until decisions are out, and
    /// they are closed identically. That a decision has been made is allowed
    /// to show — <see cref="ProfileEditing"/> already shows it by locking the
    /// form at the same moment — but which decision it is, never.
    /// </para>
    /// </param>
    public static bool IsOpen(ApplicationStatus status, bool decisionsAnnounced) =>
        StatusTransition.IsAllowed(status, ApplicationStatus.Withdrawn)
        && (decisionsAnnounced
            || status is not (ApplicationStatus.Accepted or ApplicationStatus.Rejected
                or ApplicationStatus.Waitlisted));

    /// <summary>
    /// Whether this application is already closed at the applicant's own
    /// request, so asking again is asking for what they already have.
    /// </summary>
    /// <remarks>
    /// Only <c>withdrawn</c>, and deliberately not <c>declined</c>. The two
    /// read as one word to the applicant — <see cref="ApplicantView"/> calls
    /// both of them "Withdrawn" — but they are different events with different
    /// consequences, and a spot given back through an RSVP has already moved
    /// down the waitlist. Folding them together here would be this file having
    /// an opinion about what happened, which is the history's job.
    /// </remarks>
    public static bool AlreadyWithdrawn(ApplicationStatus status) =>
        status is ApplicationStatus.Withdrawn;

    /// <summary>
    /// Why they cannot, or null while they can.
    /// </summary>
    /// <remarks>
    /// A sentence rather than a bare refusal, matching
    /// <see cref="Rsvp.WhyClosed"/> and <see cref="ProfileEditing.WhyClosed"/>.
    /// Every one of these is said to somebody who just pressed a button and
    /// needs to know whether to press it again, write to us, or stop.
    /// <para>
    /// The wording never names the internal status, and the three decided
    /// states share <see cref="WithTheTeam"/> word for word until decisions are
    /// out. A refusal message is a screen like any other, and an applicant
    /// comparing screens with a friend is the threat the whole mapping exists
    /// for.
    /// </para>
    /// <para>
    /// Every sentence here ends somewhere a person can go. Withdrawing is the
    /// control an applicant reaches for when something has changed in their
    /// life, and being told "no" with nothing after it is what turns a closed
    /// button into an email an organizer has to answer anyway.
    /// </para>
    /// </remarks>
    public static string? WhyClosed(ApplicationStatus status, bool decisionsAnnounced)
    {
        if (IsOpen(status, decisionsAnnounced))
        {
            return null;
        }

        // Before the announcement a decided application must read exactly like
        // an undecided one, and that includes why it cannot be withdrawn from
        // this screen. Checked before the switch so no per-status sentence
        // below can be reached by somebody who has not been told yet.
        if (!decisionsAnnounced && status is ApplicationStatus.Accepted
            or ApplicationStatus.Rejected or ApplicationStatus.Waitlisted)
        {
            return WithTheTeam;
        }

        return status switch
        {
            // Said in the applicant's words rather than the column's. "You
            // withdrew" is the whole of what happened and there is nothing to
            // add: the endpoint treats a second attempt as the first one
            // arriving twice, so this sentence is read on a screen rather than
            // handed back as an error.
            ApplicationStatus.Withdrawn =>
                "Your application is already closed at your request.",

            // Declining released the spot through the RSVP, which the
            // lifecycle makes final. The offer to email is not a formality:
            // an organizer can act on it, and this portal cannot.
            ApplicationStatus.Declined =>
                "You have already told us you cannot make it, so there is "
                + "nothing left to withdraw. Email us if that was a mistake.",

            // Kept apart from the sentence above even though both are closed
            // applications, because this one is not something they did. Being
            // told "at your request" about a decision that was ours would be
            // the portal getting the story wrong to somebody who knows better.
            ApplicationStatus.Rejected =>
                "We have already made a decision on this application, so there "
                + "is nothing to withdraw.",

            // The spot lapsed rather than being turned down, so the door is
            // not quite shut: the lifecycle can put an expired application
            // back to accepted, and an organizer is the one who does it.
            ApplicationStatus.Expired =>
                "The window to confirm your spot has closed, so there is "
                + "nothing to withdraw. Email us if you still want to come.",

            // They are at the event. Whatever they need now is a person at the
            // desk rather than a button, and a withdrawal after arrival would
            // erase somebody we know was here.
            ApplicationStatus.CheckedIn =>
                "You are checked in, so there is nothing to withdraw. Find an "
                + "organizer if you need to leave early.",

            _ => WithTheTeam,
        };
    }

    /// <summary>
    /// Said to every applicant whose decision has been made and not yet told.
    /// </summary>
    /// <remarks>
    /// One constant rather than the same sentence written three times, for the
    /// reason <see cref="Rsvp"/> and <see cref="ApplicantView"/> each keep one:
    /// the cases it covers have to stay identical to the character. It is also
    /// the fallback for any status this switch has not thought of, so a new
    /// state added to the lifecycle refuses safely rather than saying something
    /// specific about a row nobody has written the words for yet.
    /// <para>
    /// It says "closed" rather than the obvious past participle of this file's
    /// own name, and that is not only tone. The stored spelling of the terminal
    /// status is that word in lower case, and <c>PortalTests</c> checks every
    /// wire spelling against every byte this API sends — a sentence containing
    /// one would be the internal vocabulary reaching a screen through the back
    /// door, which is the failure the whole mapping exists to prevent.
    /// </para>
    /// </remarks>
    private const string WithTheTeam =
        "Your application is with the team now, so it cannot be closed from "
        + "this page. Email us and we will close it for you.";
}
