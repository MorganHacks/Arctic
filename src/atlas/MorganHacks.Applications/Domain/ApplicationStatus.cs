namespace MorganHacks.Applications.Domain;

/// <summary>
/// Where an application is in its life.
/// </summary>
/// <remarks>
/// Three of these are routinely conflated and each conflation costs an
/// accurate headcount:
/// <list type="bullet">
/// <item><c>Accepted</c> — we offered them a spot.</item>
/// <item><c>Confirmed</c> — they told us they are coming.</item>
/// <item><c>CheckedIn</c> — they physically arrived.</item>
/// </list>
/// <c>Confirmed</c> is the number to order food and shirts against.
/// <c>Accepted</c> is always higher and always a lie.
/// </remarks>
public enum ApplicationStatus
{
    /// <summary>Started but not submitted. The form autosaves, so this is a real row.</summary>
    Incomplete,
    Submitted,
    UnderReview,
    Accepted,
    Rejected,
    Waitlisted,
    Confirmed,
    Declined,

    /// <summary>The RSVP deadline passed without an answer. Set by the system, silently.</summary>
    Expired,
    CheckedIn,

    /// <summary>They asked to be removed. Reachable from anything before check-in.</summary>
    Withdrawn,
}

public static class ApplicationStatuses
{
    /// <summary>
    /// The stored spelling. Kept explicit rather than derived from the enum
    /// name, because renaming a C# member should never silently rewrite what
    /// a column means in rows that already exist.
    /// </summary>
    public static string ToWire(this ApplicationStatus status) => status switch
    {
        ApplicationStatus.Incomplete => "incomplete",
        ApplicationStatus.Submitted => "submitted",
        ApplicationStatus.UnderReview => "under_review",
        ApplicationStatus.Accepted => "accepted",
        ApplicationStatus.Rejected => "rejected",
        ApplicationStatus.Waitlisted => "waitlisted",
        ApplicationStatus.Confirmed => "confirmed",
        ApplicationStatus.Declined => "declined",
        ApplicationStatus.Expired => "expired",
        ApplicationStatus.CheckedIn => "checked_in",
        ApplicationStatus.Withdrawn => "withdrawn",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <summary>
    /// Reads a stored status back.
    /// </summary>
    /// <remarks>
    /// Throws on anything unrecognised rather than falling back to a default.
    /// A status we cannot name is one we cannot reason about, and quietly
    /// treating it as <c>Incomplete</c> would mean deciding somebody's
    /// application on a value we did not understand.
    /// </remarks>
    public static ApplicationStatus Parse(string wire) => wire switch
    {
        "incomplete" => ApplicationStatus.Incomplete,
        "submitted" => ApplicationStatus.Submitted,
        "under_review" => ApplicationStatus.UnderReview,
        "accepted" => ApplicationStatus.Accepted,
        "rejected" => ApplicationStatus.Rejected,
        "waitlisted" => ApplicationStatus.Waitlisted,
        "confirmed" => ApplicationStatus.Confirmed,
        "declined" => ApplicationStatus.Declined,
        "expired" => ApplicationStatus.Expired,
        "checked_in" => ApplicationStatus.CheckedIn,
        "withdrawn" => ApplicationStatus.Withdrawn,
        _ => throw new ArgumentException($"Unknown application status '{wire}'.", nameof(wire)),
    };
}
