namespace MorganHacks.Applications.Domain;

/// <summary>
/// When an applicant may still replace their own resume, and what to tell them
/// when they may not.
/// </summary>
/// <remarks>
/// A rule rather than a screen state, for the same reason as
/// <see cref="ProfileEditing"/>: the portal disables the picker from this, the
/// endpoint refuses the write from this, and the SQL narrows on the same set.
/// A check the API can be talked out of is decoration.
/// <para>
/// <b>This set is deliberately wider than <see cref="ProfileEditing.Open"/>,
/// and the difference is the whole point of this file existing.</b> The two
/// rules answer different questions and it would be a mistake to share one.
/// </para>
/// <para>
/// The profile closes at the decision because of what the profile is
/// <i>for</i>: shirt size and dietary needs are read to place orders, and name
/// and school are read at the door, so a late edit is a change to something
/// already acted on. A resume is not read by any of that. It is read by
/// sponsors, at and after the event, and by reviewers before it — and for the
/// sponsor half, the version somebody uploaded in October is the wrong one by
/// February. An accepted hacker who has since graduated, changed their major
/// or finished an internship has a real reason to hand us a newer file, and
/// telling them to email an organizer a PDF is how a resume ends up in an
/// inbox instead of in the private container this platform built for it.
/// </para>
/// <para>
/// <b>What constrains the set is the leak, not the usefulness.</b>
/// <see cref="ProfileEditing"/> closes accepted, rejected and waitlisted
/// together on purpose, so that the form locking cannot be read as a decision
/// before the announcement. That constraint applies here identically and is
/// stronger than any argument about which of those three has a use for a fresh
/// resume: if the upload worked for an accepted applicant and refused a
/// rejected one, then two friends comparing screens in the week before results
/// have been told the answer by a disabled button. So the decided three move
/// as one, and because accepted has to be open, all three are.
/// </para>
/// <para>
/// What is left closed is only the states an applicant already knows they are
/// in, where a refusal tells them nothing they were not told directly.
/// <c>Declined</c> and <c>Withdrawn</c> are their own doing. <c>Expired</c> is
/// only ever reached from <c>Accepted</c>, so somebody seeing it was told they
/// were in and let the deadline pass — the refusal reveals nothing, and there
/// is nobody left to show the file to.
/// </para>
/// </remarks>
public static class ResumeEditing
{
    /// <summary>
    /// The statuses in which the resume is still the applicant's to replace.
    /// </summary>
    /// <remarks>
    /// Written out in full rather than as <see cref="ProfileEditing.Open"/>
    /// plus extras. Somebody reading this needs to see the whole set at once
    /// to check the leak argument above, and a union expression hides exactly
    /// the members the argument is about.
    /// </remarks>
    public static readonly IReadOnlySet<ApplicationStatus> Open =
        new HashSet<ApplicationStatus>
        {
            ApplicationStatus.Incomplete,
            ApplicationStatus.Submitted,
            ApplicationStatus.UnderReview,

            // The decided three, open together or not at all. See the leak
            // argument on the class: splitting them turns a disabled button
            // into an early announcement.
            ApplicationStatus.Accepted,
            ApplicationStatus.Rejected,
            ApplicationStatus.Waitlisted,

            // The people sponsors will actually meet, which is the reason any
            // of this is worth building.
            ApplicationStatus.Confirmed,
            ApplicationStatus.CheckedIn,
        };

    /// <summary>The stored spellings of <see cref="Open"/>, for a SQL predicate.</summary>
    public static string[] OpenWire { get; } = [.. Open.Select(s => s.ToWire())];

    public static bool IsOpen(ApplicationStatus status) => Open.Contains(status);

    /// <summary>
    /// Why the picker is closed, or null while it is open.
    /// </summary>
    /// <remarks>
    /// A reason rather than a greyed-out control, exactly as
    /// <see cref="ProfileEditing.WhyClosed"/> does it. Unlike that one this
    /// needs no careful wording around the decided states, because none of
    /// them are closed here — every sentence below is for a state the
    /// applicant was already told about.
    /// </remarks>
    public static string? WhyClosed(ApplicationStatus status) => status switch
    {
        _ when IsOpen(status) => null,

        // COPY: needs sign-off.
        ApplicationStatus.Declined or ApplicationStatus.Withdrawn =>
            "Your application is closed, so there is nothing to attach a "
            + "resume to. Email us if that was a mistake.",

        // COPY: needs sign-off.
        ApplicationStatus.Expired =>
            "The window to confirm your spot has closed, so your resume is "
            + "locked. Email us if you still want to come.",

        // COPY: needs sign-off.
        _ => "Your resume is locked. Email us and we will change it for you.",
    };
}
