using System.Text.Json;

namespace MorganHacks.Applications.Forms;

/// <summary>
/// Recording an answer to a form that nobody had to sign in to fill in.
/// </summary>
/// <remarks>
/// Deliberately not a method on <see cref="IRespondentStore"/>, even though
/// both write <c>applications.form_submissions</c>. That interface says of
/// itself that every method is scoped to one person and one event and that
/// none of them takes an id from a request, and both halves of that sentence
/// are load-bearing — it is what makes the signed-in path unable to file an
/// answer against somebody the caller named. This write has no person, no
/// event, and takes a key straight out of the request body. Putting it there
/// would turn that paragraph into a lie, and the next person to read it would
/// believe the old version.
/// <para>
/// The two writers therefore share a table and not a class. What keeps them
/// from colliding is that each owns one arbiter index and says which: the
/// signed-in path upserts on (form_id, person_id), this one on (form_id,
/// submission_key), and the check constraints in 0027 refuse a row that
/// carries both.
/// </para>
/// </remarks>
public interface IAnonymousSubmissionStore
{
    /// <summary>
    /// Stores one anonymous answer and returns its id.
    /// </summary>
    /// <param name="submissionKey">
    /// Which submission attempt this is, as the page minted it, or null.
    /// <para>
    /// A repeat carrying the same key replaces the row rather than adding one,
    /// which is what makes a double-tapped Submit and a retry after a dropped
    /// response harmless. Anything else — including two submissions with
    /// identical answers and no key — is its own response, because anonymously
    /// there is no way to tell one person answering twice from two people
    /// answering the same way, and every guess at it discards somebody's
    /// words.
    /// </para>
    /// <para>
    /// The key comes from the caller, so it is trusted for exactly one thing:
    /// collapsing a repeat of the caller's own submission. It never decides
    /// who an answer belongs to, because nobody does — that is what makes this
    /// safe to take from a request at all.
    /// </para>
    /// </param>
    Task<Guid> RecordAsync(
        Guid formId,
        int formVersion,
        Guid? submissionKey,
        IReadOnlyDictionary<string, JsonElement> answers,
        CancellationToken ct = default);
}
