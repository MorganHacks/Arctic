using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Services;

/// <summary>
/// One applicant's own application, as the portal needs it.
/// </summary>
/// <remarks>
/// The internal status is carried here because the mapping to applicant-facing
/// words happens at the edge, where the announcement flag and the event dates
/// are also known. It must not survive past that: nothing that leaves the API
/// contains <see cref="Status"/>.
/// </remarks>
public sealed record ApplicantApplication(
    Guid Id,
    Guid EventId,
    ApplicationStatus Status,
    bool DecisionsAnnounced,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? RsvpDeadline,
    DateTimeOffset? EventStartsAt,
    ApplicantProfile Profile);

/// <summary>Why a profile write did not happen.</summary>
public enum ProfileSave
{
    Saved,

    /// <summary>They have not started an application to edit.</summary>
    NoApplication,

    /// <summary>The application has moved past the point where they own it.</summary>
    Closed,
}

/// <summary>
/// The reads and the one write the hacker portal needs.
/// </summary>
/// <remarks>
/// Separate from <see cref="IApplicationStore"/>, which is the organizers'
/// surface, because the two have opposite defaults. Every method here takes a
/// person id and scopes to it; nothing here can be asked for an application by
/// its own id, so there is no call an endpoint could make that reads somebody
/// else's row by accident.
/// </remarks>
public interface IApplicantPortalStore
{
    /// <summary>
    /// The application belonging to this person, or null.
    /// </summary>
    /// <remarks>
    /// Scoped by person id and nothing else. The most recent one when there is
    /// more than one, which happens the year somebody applies again — the
    /// portal is about the cycle they are in now.
    /// </remarks>
    Task<ApplicantApplication?> FindForPersonAsync(Guid personId, CancellationToken ct = default);

    /// <summary>
    /// Replaces the six profile fields, and only while the applicant still
    /// owns them.
    /// </summary>
    /// <remarks>
    /// The status test is inside the same statement as the write rather than a
    /// check the caller makes first. A read-then-write loses the race against
    /// a reviewer deciding the application in between, and the row that loses
    /// it is the one where somebody edits their name after acceptance.
    /// </remarks>
    Task<ProfileSave> SaveProfileAsync(
        Guid personId, ApplicantProfile profile, CancellationToken ct = default);
}
