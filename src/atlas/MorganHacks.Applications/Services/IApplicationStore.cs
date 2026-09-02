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
/// The resume attached to an application, as far as the database knows.
/// </summary>
/// <remarks>
/// A key and a name, and no way to read the bytes. Resolving the key is
/// <see cref="IResumeStore"/>'s job, and keeping the two apart is what stops a
/// query that happens to select this record from also handing out a link.
/// </remarks>
public sealed record StoredResume(string StorageKey, string Filename, int? Size);

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

    /// <summary>
    /// The resume on an application, or null when there is not one.
    /// </summary>
    /// <remarks>
    /// Null covers both an application nobody uploaded a resume for and an id
    /// that names no application. The caller is an organizer who already holds
    /// <c>applications.view_resume</c>, so there is nothing to hide from them —
    /// they simply have one answer to handle rather than two.
    /// </remarks>
    Task<StoredResume?> ResumeOfAsync(Guid applicationId, CancellationToken ct = default);
}
