using System.Text.Json;
using MorganHacks.Applications.Services;

namespace MorganHacks.Applications.Forms;

/// <summary>
/// Where one page of responses starts.
/// </summary>
/// <remarks>
/// A position rather than a count. The review queue is ordered newest first
/// and grows at exactly that end, so an OFFSET shifts under a reader while
/// registration is still open — every submission that lands mid-read pushes a
/// row from one page onto the next, which shows one applicant twice and skips
/// another. Naming the last row read instead is stable no matter what arrives.
/// <para>
/// Both halves are needed. Two applications can share a <c>submitted_at</c> to
/// the microsecond — a launch meeting where a room submits at once is exactly
/// that traffic — and a cursor on the timestamp alone either loses the second
/// one or repeats it forever.
/// </para>
/// </remarks>
public readonly record struct ResponseCursor(DateTimeOffset SubmittedAt, Guid Id);

/// <summary>
/// One submitted form, with its answers keyed by question.
/// </summary>
/// <remarks>
/// Keyed by <see cref="FormField.Key"/> and never by position or label. A form
/// edited between two submissions has different questions in different places
/// with different wording, and the key is the one thing that survives all
/// three — it is generated once when a question is added and never
/// regenerated, precisely so the answers already given still line up.
/// <para>
/// Answers are <see cref="JsonElement"/> whichever half of the row they came
/// from, so a caller does not have to know or care that some of them were
/// columns.
/// </para>
/// </remarks>
/// <param name="Resume">
/// The resume, or null. Carries a storage key and no way to resolve it — that
/// is <see cref="IResumeStore"/>'s job, and keeping the two apart is what stops
/// a query that happens to select this also handing out a link.
/// </param>
public sealed record FormResponse(
    Guid Id,
    DateTimeOffset SubmittedAt,
    int FormVersion,
    IReadOnlyDictionary<string, JsonElement> Answers,
    StoredResume? Resume);

/// <summary>A page of responses, and where the next one begins.</summary>
/// <remarks>
/// <see cref="Next"/> is null on the last page rather than on the page after
/// it. The store reads one row more than it returns to know the difference,
/// which costs one row and saves every caller a round trip that comes back
/// empty.
/// </remarks>
public sealed record ResponsePage(IReadOnlyList<FormResponse> Items, ResponseCursor? Next);

/// <summary>Reading back what applicants answered.</summary>
/// <remarks>
/// Separate from <see cref="ISubmissionStore"/>, which writes. They touch the
/// same table and have opposite risks: the write path's job is to refuse a
/// caller who did not use the page, and this one's is to never hand an answer
/// to somebody who should not read it.
/// </remarks>
public interface IResponseStore
{
    /// <summary>One page, newest first.</summary>
    Task<ResponsePage> PageAsync(
        Guid eventId,
        FormQuestions questions,
        ResponseCursor? after,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// One response, or null.
    /// </summary>
    /// <remarks>
    /// The event is part of the lookup rather than checked afterwards. Without
    /// it a response id from one cycle read through another cycle's form would
    /// answer, and the form in the URL is the only thing the caller was
    /// authorized against.
    /// </remarks>
    Task<FormResponse?> ByIdAsync(
        Guid eventId,
        Guid responseId,
        FormQuestions questions,
        CancellationToken ct = default);

    /// <summary>
    /// Every response, in the same order, streamed.
    /// </summary>
    /// <remarks>
    /// Streamed rather than returned as a list because this backs the export,
    /// and the export is the one read whose size is the whole applicant pool
    /// rather than a screenful. What the caller does with the rows is its own
    /// decision; this side's job is not to materialise several hundred answer
    /// sets in order to hand them over one at a time.
    /// </remarks>
    IAsyncEnumerable<FormResponse> AllAsync(
        Guid eventId,
        FormQuestions questions,
        CancellationToken ct = default);
}
