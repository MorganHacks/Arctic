namespace MorganHacks.Applications.Domain;

/// <summary>What the check-in screen says, whether or not there is a code on it.</summary>
public sealed record CheckInWords(string Heading, string Explanation, string? Hint);

/// <summary>
/// The words on the applicant's check-in screen.
/// </summary>
/// <remarks>
/// Beside <see cref="ApplicantView"/> and for the same reason: this is
/// applicant-facing copy, it has to be changeable in one place once the team
/// has signed it off, and a screen that writes its own version of a sentence
/// is a screen that eventually disagrees with the one they signed.
/// <para>
/// The announcement gate is repeated here rather than inferred. A page that
/// said "confirm your spot and your code appears here" would be telling
/// somebody they were accepted, days before the team meant to, through a
/// screen nobody thought of as a decision letter.
/// </para>
/// </remarks>
public static class CheckInView
{
    /// <summary>
    /// Describes the check-in screen for one applicant.
    /// </summary>
    /// <param name="status">The internal status, or null when there is no application.</param>
    /// <param name="decisionsAnnounced">
    /// Whether decisions have been released. Until they are, an accepted
    /// applicant reads exactly what an undecided one reads.
    /// </param>
    public static CheckInWords Describe(
        ApplicationStatus? status, bool decisionsAnnounced = false)
    {
        if (status is null)
        {
            return new CheckInWords(
                Heading,
                "You have not started an application yet.",
                null);
        }

        if (status is ApplicationStatus.CheckedIn)
        {
            return new CheckInWords(
                "You are checked in",
                "Your code has been scanned. There is nothing left to do with it.",
                "It stays on this screen in case somebody asks to see it again.");
        }

        if (status is ApplicationStatus.Confirmed)
        {
            return new CheckInWords(
                Heading,
                "Show this when you arrive. A volunteer scans the square, or "
                + "types the twelve characters under it.",
                "This code does not change, so a screenshot of this screen "
                + "works just as well. Your phone does not need a signal for "
                + "it to be read.");
        }

        if (status is ApplicationStatus.Accepted && decisionsAnnounced)
        {
            return new CheckInWords(
                Heading,
                "Confirm your spot and your code appears here.",
                null);
        }

        // Everything else shares one sentence, including the decided states
        // before the announcement. Somebody comparing this screen with a
        // friend's learns nothing from it, which is the whole job.
        return new CheckInWords(Heading, Waiting, null);
    }

    private const string Heading = "Your check-in code";

    /// <summary>
    /// Said to everybody who does not have a code yet, decided or not.
    /// </summary>
    /// <remarks>
    /// One constant rather than the same sentence written several times, for
    /// the reason <see cref="ApplicantView"/> keeps its own: the cases it
    /// covers have to stay word for word identical.
    /// </remarks>
    private const string Waiting =
        "Your code appears here once you have confirmed a spot.";
}
