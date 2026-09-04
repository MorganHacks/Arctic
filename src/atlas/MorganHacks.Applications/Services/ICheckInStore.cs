using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Services;

/// <summary>
/// Who a check-in code belongs to, as the door needs them.
/// </summary>
/// <remarks>
/// A name and a status and nothing else. The volunteer's question is whether
/// the person in front of them may come in and whether they are already
/// counted; a school, a phone number and a set of dietary needs are none of
/// their business at that moment and would be on a screen held up in a public
/// doorway.
/// <para>
/// The name is here for the one thing entropy cannot do. A code that does not
/// rotate can be forwarded, and what catches that is a volunteer reading a
/// name off a screen while looking at the person who handed them the phone.
/// </para>
/// </remarks>
public sealed record CheckInSubject(
    Guid ApplicationId,
    ApplicationStatus Status,
    string? FirstName,
    string? LastName,
    DateTimeOffset? CheckedInAt);

/// <summary>
/// The one read the check-in desk needs.
/// </summary>
/// <remarks>
/// Its own store rather than a method on <see cref="IApplicationStore"/>,
/// which owns the lifecycle, or on <see cref="IApplicantStore"/>, which is the
/// registration team's list. This is a single indexed lookup that has to
/// answer in front of a queue, and it is reached with a different permission
/// by people who hold nothing else.
/// <para>
/// There is deliberately no write here. Checking somebody in is a status
/// change, and there is one writer for those.
/// </para>
/// </remarks>
public interface ICheckInStore
{
    /// <summary>
    /// The application carrying this code, or null when nothing does.
    /// </summary>
    /// <remarks>
    /// Takes the canonical twelve characters. Normalising what a scanner or a
    /// keyboard produced is the caller's job, so this cannot be the place a
    /// lookup silently succeeds against a shape the database would never hold.
    /// </remarks>
    Task<CheckInSubject?> FindByCodeAsync(string code, CancellationToken ct = default);
}
