using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Services;

/// <summary>One recorded step in an application's life.</summary>
public sealed record StatusChange(
    Guid ApplicationId,
    ApplicationStatus? From,
    ApplicationStatus To,
    Guid? ActorId,
    string? Reason,
    Guid? BatchId,
    DateTimeOffset At);

/// <summary>
/// The Applications module's own tables. Nothing outside this module reads them.
/// </summary>
public interface IApplicationStore
{
    Task<Guid> StartAsync(
        Guid eventId, string email, Guid? personId = null, CancellationToken ct = default);

    Task<ApplicationStatus?> StatusOfAsync(Guid applicationId, CancellationToken ct = default);

    /// <summary>
    /// Moves an application to a new status, or throws if the lifecycle does
    /// not allow it.
    /// </summary>
    /// <remarks>
    /// The only way to change a status. There is deliberately no setter and no
    /// "update application" method that happens to accept one: validation a
    /// caller can go around is decorative.
    /// </remarks>
    Task<StatusChange> TransitionAsync(
        Guid applicationId,
        ApplicationStatus next,
        Guid? actorId = null,
        string? reason = null,
        Guid? batchId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<StatusChange>> HistoryOfAsync(
        Guid applicationId, CancellationToken ct = default);
}
