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
    /// <summary>
    /// Reads a stored status back, without throwing.
    /// </summary>
    /// <remarks>
    /// For the places the string arrived from outside — a query parameter, a
    /// request body, a trigger an organizer configured — where an unrecognised
    /// value is a caller to answer rather than a fault to raise.
    /// <see cref="Parse"/> stays the one for reading our own column, where an
    /// unknown value really is something to stop on.
    /// <para>
    /// Here rather than copied into each endpoint, which is how it was: two
    /// handlers had a private loop over <see cref="Enum.GetValues{TEnum}"/>
    /// comparing <see cref="ToWire"/>, and a third was about to. Three copies
    /// of one mapping is three chances for one of them to accept a spelling
    /// the column does not.
    /// </para>
    /// </remarks>
    public static bool TryParse(string? wire, out ApplicationStatus status)
    {
        foreach (var candidate in Enum.GetValues<ApplicationStatus>())
        {
            if (candidate.ToWire() == wire)
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }

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

/// <summary>Who the applicant portal is for.</summary>
/// <remarks>
/// Accepted, confirmed, checked in. The portal exists to carry somebody from
/// a decision to a door on the day -- the venue, the schedule, the Discord
/// invite an organizer posts an hour before doors -- so the people it is for
/// are the people who are coming.
/// <para>
/// This was not enforced anywhere until now. The group required a session and
/// a feature flag and nothing else, so anybody who had ever applied could sign
/// in and read every announcement for the event. RSVP and the check-in code
/// were already gated, which is what made it look right from the organizer
/// side: the writes were protected and the reading was not.
/// </para>
/// <para>
/// Waitlisted is deliberately out. A waitlisted applicant is not coming yet,
/// and the announcements they would read are addressed to people who are --
/// promoting somebody to accepted is what lets them in, which is the decision
/// an organizer already makes.
/// </para>
/// </remarks>
public static class PortalAccess
{
    public static readonly IReadOnlySet<ApplicationStatus> Allowed =
        new HashSet<ApplicationStatus>
        {
            ApplicationStatus.Accepted,
            ApplicationStatus.Confirmed,
            ApplicationStatus.CheckedIn,
        };

    /// <summary>The stored spellings, for a SQL predicate.</summary>
    public static string[] AllowedWire { get; } = [.. Allowed.Select(s => s.ToWire())];
}
