namespace MorganHacks.Applications.Domain;

/// <summary>Thrown when a status change is not one the lifecycle allows.</summary>
public sealed class InvalidTransitionException(ApplicationStatus from, ApplicationStatus to)
    : InvalidOperationException($"An application cannot go from {from} to {to}.")
{
    public ApplicationStatus From { get; } = from;
    public ApplicationStatus To { get; } = to;
}

/// <summary>
/// Which status changes the lifecycle permits.
/// </summary>
/// <remarks>
/// The table is the specification. Reading it should answer "can a rejected
/// applicant be accepted later" without reading any other file.
/// </remarks>
public static class StatusTransition
{
    private static readonly Dictionary<ApplicationStatus, ApplicationStatus[]> Allowed = new()
    {
        [ApplicationStatus.Incomplete] = [ApplicationStatus.Submitted, ApplicationStatus.Withdrawn],
        [ApplicationStatus.Submitted] = [ApplicationStatus.UnderReview, ApplicationStatus.Withdrawn],
        [ApplicationStatus.UnderReview] =
        [
            ApplicationStatus.Accepted, ApplicationStatus.Rejected,
            ApplicationStatus.Waitlisted, ApplicationStatus.Withdrawn,
        ],
        [ApplicationStatus.Waitlisted] =
        [
            ApplicationStatus.Accepted, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn,
        ],
        [ApplicationStatus.Accepted] =
        [
            ApplicationStatus.Confirmed, ApplicationStatus.Declined,
            ApplicationStatus.Expired, ApplicationStatus.Withdrawn,
        ],
        [ApplicationStatus.Confirmed] = [ApplicationStatus.CheckedIn, ApplicationStatus.Withdrawn],

        // Reinstatement, for somebody who missed the deadline and got in
        // touch. Deliberately manual: nothing should un-expire on its own.
        [ApplicationStatus.Expired] = [ApplicationStatus.Accepted],

        // Terminal. Reversing any of these is a new application, not an edit,
        // because the history has to keep saying what actually happened.
        [ApplicationStatus.Declined] = [],
        [ApplicationStatus.Rejected] = [],
        [ApplicationStatus.CheckedIn] = [],
        [ApplicationStatus.Withdrawn] = [],
    };

    public static bool IsAllowed(ApplicationStatus from, ApplicationStatus to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);

    /// <summary>Throws unless the lifecycle permits this change.</summary>
    public static void Validate(ApplicationStatus from, ApplicationStatus to)
    {
        if (!IsAllowed(from, to))
        {
            throw new InvalidTransitionException(from, to);
        }
    }

    /// <summary>Where an application in this status can still go.</summary>
    public static IReadOnlyList<ApplicationStatus> From(ApplicationStatus status) =>
        Allowed.TryGetValue(status, out var next) ? next : [];
}
