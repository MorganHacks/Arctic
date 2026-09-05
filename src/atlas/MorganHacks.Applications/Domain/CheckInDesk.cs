namespace MorganHacks.Applications.Domain;

/// <summary>
/// What a scan did, in one word.
/// </summary>
/// <remarks>
/// Named rather than inferred from the status code, because two of these are
/// 200 and a screen has to tell them apart without parsing English.
/// </remarks>
public enum ScanOutcome
{
    /// <summary>They were confirmed, and they are now checked in.</summary>
    CheckedIn,

    /// <summary>They were already checked in. Still let them through.</summary>
    AlreadyCheckedIn,

    /// <summary>No application carries that code.</summary>
    UnknownCode,

    /// <summary>There is an application, and it is not one that may be checked in.</summary>
    NotConfirmed,
}

/// <summary>
/// The sentences a volunteer reads off a scan.
/// </summary>
/// <remarks>
/// Written for somebody standing in a doorway with a queue behind them, so
/// every refusal names an action. "Not eligible" is not an answer anybody can
/// do anything with at seven in the morning.
/// <para>
/// Deliberately vaguer than the codebase knows. A volunteer's screen is held
/// at chest height in front of the person it is about, and the difference
/// between "they were rejected" and "they never answered" is not something to
/// put on it. Only the accepted case gets its own sentence, because that one
/// is the only refusal with a fix that does not start with finding somebody.
/// </para>
/// </remarks>
public static class CheckInDesk
{
    /// <summary>What to say about a scan that worked.</summary>
    public static string Describe(ScanOutcome outcome) => outcome switch
    {
        ScanOutcome.CheckedIn => "Checked in.",
        ScanOutcome.AlreadyCheckedIn =>
            "Already checked in. Let them through.",
        ScanOutcome.UnknownCode =>
            "That code is not one of ours. Ask them to open their portal and "
            + "read the code from there.",
        ScanOutcome.NotConfirmed => NotConfirmed,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    /// <summary>Why this person cannot be checked in, and what to do about it.</summary>
    /// <remarks>
    /// Follows <see cref="StatusTransition"/> rather than restating it: the
    /// only status that may become <c>checked_in</c> is <c>confirmed</c>, so
    /// everything reaching here is a refusal and the only question is which
    /// sentence.
    /// </remarks>
    public static string WhyNot(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Accepted =>
            "They have a spot but have not confirmed it. An organizer can "
            + "confirm it for them.",
        _ => NotConfirmed,
    };

    private const string NotConfirmed =
        "They are not confirmed for this event. Send them to an organizer.";
}
