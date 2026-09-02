namespace MorganHacks.Applications.Services;

/// <summary>An event, as a screen that only has to name one needs it.</summary>
public sealed record EventSummary(
    Guid Id, string Slug, string Name, DateTimeOffset? StartsAt);

/// <summary>
/// Reading the events everything else is scoped to.
/// </summary>
/// <remarks>
/// Deliberately read-only and deliberately small. Nothing creates an event
/// through the API yet — there is one a year and it is made by hand — but
/// every admin screen that touches applications has to say which event it is
/// showing, and a console cannot ask that question without a list to ask it
/// from.
/// </remarks>
public interface IEventStore
{
    /// <summary>Newest first, so the one being run now is first.</summary>
    Task<IReadOnlyList<EventSummary>> ListAsync(CancellationToken ct = default);
}
