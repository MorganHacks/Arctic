using System.Text.Json;

namespace MorganHacks.Applications.Forms;

/// <summary>
/// Thrown when this address has already applied to this event.
/// </summary>
/// <remarks>
/// Raised from the unique index rather than from a lookup. Checking first and
/// inserting after is two statements with a gap between them, and the gap is
/// exactly wide enough for somebody who double-tapped Submit on a slow phone
/// connection.
/// </remarks>
public sealed class DuplicateApplicationException()
    : InvalidOperationException("An application already exists for this address.");

/// <summary>
/// Thrown when the published form has no question that could hold an address.
/// </summary>
/// <remarks>
/// A form problem rather than an applicant's, so it is separated from
/// validation: nothing they type can fix it, and the person who needs telling
/// is whoever published the form.
/// </remarks>
public sealed class FormCannotCreateApplicantsException()
    : InvalidOperationException("This form does not ask for an email address.");

/// <summary>Where a completed form ends up.</summary>
public interface ISubmissionStore
{
    /// <summary>
    /// Turns a set of answers into a submitted application.
    /// </summary>
    /// <remarks>
    /// The version is passed in rather than looked up, so the questions the
    /// answers were validated against and the questions recorded against the
    /// row are the same object. Reading it twice invites them to differ across
    /// a publish that lands mid-request.
    /// </remarks>
    Task<Guid> SubmitApplicationAsync(
        Form form,
        FormVersion version,
        IReadOnlyDictionary<string, JsonElement> answers,
        CancellationToken ct = default);
}
