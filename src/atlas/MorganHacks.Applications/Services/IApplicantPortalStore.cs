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

/// <summary>
/// One notice, in the only shape an applicant ever sees one.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="Announcement"/>. There is no
/// <c>postedBy</c> here: the notice is the team speaking, not a named
/// organizer, and putting a person id on it would mean the portal knew which
/// human to be annoyed at about a schedule change. There are no retraction
/// fields either, because a retracted notice never reaches this type at all.
/// </remarks>
public sealed record PortalAnnouncement(Guid Id, string Body, DateTimeOffset PostedAt);
/// The resume currently attached to an applicant's own application.
/// </summary>
/// <remarks>
/// Deliberately without the storage key. The portal shows an applicant what we
/// are holding — the name they picked, how big it was and when it arrived —
/// and none of that needs a key. A key that reached this record would be one
/// import away from reaching a JSON response, and <c>resume_key</c> is on
/// <c>Redaction.SensitiveKeys</c> precisely because it is the one string that
/// turns "somebody's CV exists" into "here is where it lives".
/// <para>
/// There is no signed link here either, and that is a choice rather than an
/// omission. The organizers' side issues one because a reviewer has to read a
/// file they have never seen; an applicant is the person who uploaded it and
/// already has it. Every link this platform mints is another five minutes in
/// which a resume can be pasted somewhere, and one that nobody needed is the
/// cheapest one to not mint.
/// </para>
/// </remarks>
public sealed record ApplicantResume(
    string Filename, int? Size, DateTimeOffset? UploadedAt);

/// <summary>Why a resume write did not happen.</summary>
/// <remarks>
/// The same three outcomes as <see cref="ProfileSave"/> and a separate type
/// anyway. They are judged against different status sets — see
/// <see cref="ResumeEditing"/> for why the sets differ — and sharing one enum
/// is how somebody later "simplifies" the two rules into one.
/// </remarks>
public enum ResumeSave
{
    Saved,

    /// <summary>They have not started an application to attach one to.</summary>
    NoApplication,

    /// <summary>The application has moved past the point where they own it.</summary>
    Closed,
}

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

    /// <summary>
    /// This person's check-in code, minting it the first time they ask.
    /// </summary>
    /// <remarks>
    /// Null when they have no application, or when the one they have is not in
    /// a status that gets a code. A screen with no code has to say why, and
    /// that sentence is the endpoint's to choose from the status rather than
    /// this method's to encode in a second return value.
    /// <para>
    /// Minted lazily rather than at the moment somebody confirms. Hanging it
    /// off the transition would put check-in inside the one method that owns
    /// the lifecycle, and would leave every application confirmed before this
    /// shipped without a code at all. Asking for it on the screen that shows
    /// it needs no coordination and cannot be forgotten. What makes that safe
    /// is that it is idempotent: the first call creates the code and every
    /// call after it, forever, returns the same twelve characters.
    /// </para>
    /// </remarks>
    Task<string?> CheckInCodeAsync(Guid personId, CancellationToken ct = default);

    /// <summary>
    /// The live notices for the event this person is applying to, newest
    /// first.
    /// </summary>
    /// <remarks>
    /// The one read on this interface that is not about the caller's own row,
    /// which is exactly why it is worth being explicit about who may see it.
    /// <b>An announcement is visible to anybody signed in who holds an
    /// application for that event, and to nobody else.</b> That is not a
    /// policy this method trusts a caller to apply — it is the shape of the
    /// statement. The event is not a parameter: it is resolved inside the
    /// query from the session's own most recent application, so somebody
    /// signed in with no application gets an empty list, and there is no id
    /// anywhere in the call that could be pointed at another event's feed.
    /// <para>
    /// What follows from that is a rule for the people posting rather than for
    /// this code, and it belongs written down next to the query that enforces
    /// the first half: a notice is read by every applicant at the event, so it
    /// must never carry anything true of only one of them. There is no
    /// targeting here, no recipient column and no merge-field rendering — the
    /// row is handed out exactly as it was typed — so the only way an
    /// announcement leaks something is if somebody types it, and the console
    /// that posts them is where that gets said out loud.
    /// </para>
    /// <para>
    /// Retracted notices are absent rather than marked. An applicant has no
    /// use for "this was taken down"; the correction is the notice above it.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<PortalAnnouncement>> AnnouncementsForPersonAsync(
        Guid personId, CancellationToken ct = default);
    /// What resume, if any, is on this person's own application.
    /// </summary>
    /// <remarks>
    /// Scoped by person id like everything else here, so the portal screen that
    /// shows "we have your CV" cannot be pointed at somebody else's. Null both
    /// when there is no application and when there is one with nothing
    /// attached: to the screen those are the same empty state, and telling them
    /// apart is <see cref="FindForPersonAsync"/>'s job.
    /// </remarks>
    Task<ApplicantResume?> ResumeForPersonAsync(
        Guid personId, CancellationToken ct = default);

    /// <summary>
    /// Points this person's own application at a resume that has already been
    /// written to the object store.
    /// </summary>
    /// <remarks>
    /// Takes the key rather than the bytes because the write to the object
    /// store has to happen first — there is nothing to record until there is
    /// something recorded. The key is generated by <c>IResumeStore</c> and has
    /// never been outside the process; nothing here would be safe if it had
    /// come from a request.
    /// <para>
    /// The status test is inside the same statement as the write, for the same
    /// reason <see cref="SaveProfileAsync"/> does it: a read-then-write loses
    /// the race against an organizer moving the row in between, and there is
    /// no version of this where a resume lands on an application that had
    /// already stopped accepting them.
    /// </para>
    /// </remarks>
    Task<ResumeSave> SaveResumeAsync(
        Guid personId,
        string storageKey,
        string filename,
        int size,
        CancellationToken ct = default);
}
