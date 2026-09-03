using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Services;

/// <summary>
/// Where one page of applicants starts.
/// </summary>
/// <remarks>
/// A position rather than a count, for the reason every list in this system
/// is: registration reads this screen while applications are still arriving,
/// and each one that lands pushes a row from one page onto the next. An OFFSET
/// taken before that arrival and used after it shows one applicant twice and
/// skips another, silently, in the one place where missing somebody means they
/// never get a decision.
/// <para>
/// Both halves are needed. Two applications can share a <c>created_at</c> to
/// the microsecond — a club meeting where a room starts the form at once is
/// exactly that traffic — and a cursor on the timestamp alone either loses the
/// second one or repeats it forever.
/// </para>
/// <para>
/// <c>created_at</c> and not <c>submitted_at</c>, which is what the responses
/// list is ordered by. That list holds only what was submitted; this one holds
/// every applicant, including the ones who never finished, and those have no
/// submitted_at to name.
/// </para>
/// </remarks>
public readonly record struct ApplicantCursor(DateTimeOffset CreatedAt, Guid Id);

/// <summary>
/// One applicant, as the organizers' list and header show them.
/// </summary>
/// <remarks>
/// The identity and the lifecycle, and none of the answers. What somebody
/// wrote about themselves comes back through <see cref="Forms.IResponseStore"/>
/// keyed by question, because that is the only reading of an answer that
/// survives the form being edited — and duplicating half of it into columns
/// here would give a screen two versions of the same fact to disagree about.
/// <para>
/// The exceptions are the four fields a list has to have to be scannable at
/// all: who they are, where they are from, and where they have got to. Those
/// are promoted columns rather than jsonb precisely because they get read this
/// way.
/// </para>
/// </remarks>
/// <param name="HasResume">
/// Whether there are bytes, never where they are. The storage key does not
/// leave this module — resolving one is <see cref="IResumeStore"/>'s job, and
/// keeping the two apart is what stops a query that happens to select an
/// applicant also handing out a way to read their CV.
/// </param>
public sealed record Applicant(
    Guid Id,
    Guid EventId,
    string Email,
    string? FirstName,
    string? LastName,
    string? School,
    ApplicationStatus Status,
    int FormVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? RsvpDeadline,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? DeclinedAt,
    DateTimeOffset? CheckedInAt,
    bool HasResume);

/// <summary>A page of applicants, newest first, and where the next one begins.</summary>
/// <remarks>
/// <see cref="Next"/> is null on the last page rather than on the page after
/// it. The store reads one row more than it returns to know the difference,
/// which costs one row and saves every caller a round trip that comes back
/// empty.
/// </remarks>
public sealed record ApplicantPage(IReadOnlyList<Applicant> Items, ApplicantCursor? Next);

/// <summary>
/// Which applicants a page is drawn from.
/// </summary>
/// <remarks>
/// The event is not optional. Everything in this schema is scoped to one
/// cycle, and a list that spanned two would put last year's rejections in
/// front of somebody deciding this year's.
/// </remarks>
/// <param name="Text">
/// A fragment of a name or an address, or null for all of them. Matched
/// anywhere in the value rather than at the start: registration is usually
/// working from half a surname somebody said out loud, and a prefix search
/// answers nothing for an address that begins with a first initial.
/// </param>
/// <param name="Statuses">
/// The statuses to include, or empty for all of them. A set rather than one
/// value because the useful filters are groups — everything undecided,
/// everything accepted-or-confirmed — and one-at-a-time would mean the screen
/// stitching pages together itself.
/// </param>
public sealed record ApplicantSearch(
    Guid EventId,
    string? Text = null,
    IReadOnlyList<ApplicationStatus>? Statuses = null);

/// <summary>
/// One internal note about an applicant.
/// </summary>
/// <remarks>
/// Never shown to the applicant, which is worth saying in the type as well as
/// in the schema comment that says it. The author is an id rather than an
/// address, matching every other record in this system.
/// </remarks>
public sealed record ApplicantNote(
    Guid Id, Guid AuthorId, string Body, DateTimeOffset CreatedAt);

/// <summary>
/// The organizers' view of who has applied.
/// </summary>
/// <remarks>
/// Separate from <see cref="IApplicationStore"/>, which owns the lifecycle. A
/// status change goes through that one and only that one, because the
/// validation and the transaction-local settings the history trigger reads
/// both live there — a second way to write a status would be a second way to
/// write it wrong.
/// <para>
/// This is the read side plus notes, which are the one thing an organizer
/// writes about an applicant that is not a decision.
/// </para>
/// </remarks>
public interface IApplicantStore
{
    /// <summary>One page of applicants, newest first.</summary>
    Task<ApplicantPage> PageAsync(
        ApplicantSearch search,
        ApplicantCursor? after,
        int limit,
        CancellationToken ct = default);

    /// <summary>One applicant, or null when there is no such application.</summary>
    Task<Applicant?> ByIdAsync(Guid applicationId, CancellationToken ct = default);

    /// <summary>
    /// How many applications are in each status on an event.
    /// </summary>
    /// <remarks>
    /// Every status that has a row, and no key for the ones that do not. The
    /// screen filling in the zeroes knows which statuses exist; a store that
    /// invented them would be asserting something about the lifecycle from the
    /// wrong side of the wall.
    /// </remarks>
    Task<IReadOnlyDictionary<ApplicationStatus, int>> CountsAsync(
        Guid eventId, CancellationToken ct = default);

    Task<IReadOnlyList<ApplicantNote>> NotesOfAsync(
        Guid applicationId, CancellationToken ct = default);

    /// <summary>
    /// Writes a note, or answers null when there is no such application.
    /// </summary>
    /// <remarks>
    /// Null rather than a foreign key violation surfacing as a 500. An id that
    /// names nothing is an ordinary thing for a caller to send — a stale tab,
    /// a link somebody kept — and it deserves an answer rather than an
    /// exception.
    /// </remarks>
    Task<ApplicantNote?> AddNoteAsync(
        Guid applicationId, Guid authorId, string body, CancellationToken ct = default);
}
