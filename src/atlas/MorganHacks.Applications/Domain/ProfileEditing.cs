namespace MorganHacks.Applications.Domain;

/// <summary>
/// When an applicant may still change their own details, and what to tell them
/// when they may not.
/// </summary>
/// <remarks>
/// A rule rather than a screen state. The portal disables the form from this,
/// the endpoint refuses the write from this, and the SQL narrows on the same
/// set — a check the API can be talked out of is decoration.
/// </remarks>
public static class ProfileEditing
{
    /// <summary>
    /// The statuses in which the profile is still the applicant's to change.
    /// </summary>
    /// <remarks>
    /// <c>UnderReview</c> is in this set on purpose, and it is the one
    /// judgement call here.
    /// <para>
    /// Submitted and under review are deliberately the same sentence to an
    /// applicant — that is what lets reviewers work through the queue for a
    /// week while every applicant reads the same thing. A form that locks the
    /// moment a reviewer opens the file hands that difference straight back:
    /// the applicant cannot see the status, but they can see the button stop
    /// working, and they can compare with a friend. Closing editing at the
    /// decision rather than at the first reviewer keeps the two states
    /// genuinely indistinguishable.
    /// </para>
    /// <para>
    /// Nothing downstream is harmed by a late edit. Shirt size and dietary
    /// needs are read for ordering, which happens after decisions; name and
    /// school are read at check-in, which is later still.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlySet<ApplicationStatus> Open =
        new HashSet<ApplicationStatus>
        {
            ApplicationStatus.Incomplete,
            ApplicationStatus.Submitted,
            ApplicationStatus.UnderReview,
        };

    /// <summary>The stored spellings of <see cref="Open"/>, for a SQL predicate.</summary>
    public static string[] OpenWire { get; } = [.. Open.Select(s => s.ToWire())];

    public static bool IsOpen(ApplicationStatus status) => Open.Contains(status);

    /// <summary>
    /// Why the form is closed, or null while it is open.
    /// </summary>
    /// <remarks>
    /// A reason rather than a greyed-out field. "Disabled with no explanation"
    /// is the state that generates the email, and the answer is always one
    /// sentence we could have written on the screen.
    /// <para>
    /// The wording never names the internal status, and never distinguishes
    /// the decided states from each other before decisions are announced —
    /// which is why accepted, rejected and waitlisted all share a line.
    /// </para>
    /// </remarks>
    public static string? WhyClosed(ApplicationStatus status) => status switch
    {
        _ when IsOpen(status) => null,

        // The three decided states share one sentence deliberately. Different
        // wording per outcome would tell somebody their result from the edit
        // screen, days before the announcement.
        ApplicationStatus.Accepted or ApplicationStatus.Rejected
            or ApplicationStatus.Waitlisted =>
            "Your application is with the team now, so these details are "
            + "locked. Email us and we will change them for you.",

        ApplicationStatus.Confirmed or ApplicationStatus.CheckedIn =>
            "You are on the attendee list, so these details are locked — "
            + "shirts and catering are ordered from them. Email us if "
            + "something needs to change.",

        ApplicationStatus.Declined or ApplicationStatus.Withdrawn =>
            "Your application is closed, so these details are locked. Email "
            + "us if that was a mistake.",

        ApplicationStatus.Expired =>
            "The window to confirm your spot has closed, so these details are "
            + "locked. Email us if you still want to come.",

        _ => "These details are locked. Email us and we will change them for you.",
    };
}
