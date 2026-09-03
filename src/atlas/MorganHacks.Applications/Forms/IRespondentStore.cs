using System.Text.Json;

namespace MorganHacks.Applications.Forms;

/// <summary>
/// An address that belongs to an application on this event, and nothing else.
/// </summary>
/// <remarks>
/// Deliberately thin. This is what the sign-in step is allowed to learn before
/// anybody has proved who they are: enough to send a link to an address we
/// already hold and to name the account it belongs to, and nothing about their
/// application. The status, the answers and the decision are all on the other
/// side of the link.
/// </remarks>
/// <param name="Email">
/// As stored, not as typed. The two differ in case often enough — a phone
/// capitalises the first letter — and using the stored one means the address
/// we mail and the address on the row are the same string.
/// </param>
public sealed record OnFile(Guid ApplicationId, string Email, string? FullName, Guid? PersonId);

/// <summary>
/// The reads and writes a sign-in form needs.
/// </summary>
/// <remarks>
/// Separate from <see cref="ISubmissionStore"/>, which turns a form into an
/// applicant, and from <see cref="IResponseStore"/>, which reads applications
/// back for organizers. This one is the applicant's own side of a form that is
/// not the application: every method is scoped to one person and one event,
/// and none of them takes an id from a request.
/// <para>
/// Nothing here writes <c>identity.people</c>. Creating the account an
/// applicant signs in with is Identity's job and stays there — see
/// <c>IIdentityStore.EnsureHackerAsync</c> — because a module reaching into
/// another module's tables is how the rule that organizers sign in through
/// Google gets bypassed by accident.
/// </para>
/// </remarks>
public interface IRespondentStore
{
    /// <summary>
    /// Whether this address has an application on this event.
    /// </summary>
    /// <remarks>
    /// The whole of what the email step is permitted to find out, and the
    /// caller must answer identically whether or not it finds anything. A
    /// difference in status, body or wording turns a form's sign-in box into a
    /// way to ask which addresses applied.
    /// </remarks>
    Task<OnFile?> FindOnFileAsync(Guid eventId, string email, CancellationToken ct = default);

    /// <summary>
    /// Points an application at the account its address now has.
    /// </summary>
    /// <remarks>
    /// Only when there is nothing there yet, in the same statement rather than
    /// after a read. An application already claimed by somebody must not be
    /// reassigned by an unauthenticated request, whatever the addresses say.
    /// </remarks>
    Task LinkPersonAsync(Guid applicationId, Guid personId, CancellationToken ct = default);

    /// <summary>
    /// Who is signed in, as this form's audience and prefill need them.
    /// </summary>
    /// <remarks>
    /// Null when this person has no application on this event. That is not an
    /// error: they signed in, they are simply not somebody this form is about,
    /// and there is nothing to prefill or to check an audience against.
    /// <para>
    /// The form id is passed because their last answer to this same form is
    /// part of what gets prefilled — an RSVP somebody is changing their mind
    /// about should open showing what they said last time.
    /// </para>
    /// </remarks>
    Task<Respondent?> ForPersonAsync(
        Guid eventId, Guid personId, Guid formId, CancellationToken ct = default);

    /// <summary>
    /// Records an answer against the person who gave it.
    /// </summary>
    /// <remarks>
    /// An upsert on (form, person), because the question a form asks has one
    /// current answer. Two rows would mean every reader deciding which of them
    /// counts, and the one that decides catering would eventually decide
    /// wrong.
    /// </remarks>
    Task<Guid> RecordAsync(
        Guid formId,
        int formVersion,
        Respondent respondent,
        IReadOnlyDictionary<string, JsonElement> answers,
        CancellationToken ct = default);
}
