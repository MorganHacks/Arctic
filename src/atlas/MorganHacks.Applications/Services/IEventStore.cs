namespace MorganHacks.Applications.Services;

/// <summary>An event, as a screen that only has to name one needs it.</summary>
public sealed record EventSummary(
    Guid Id, string Slug, string Name, DateTimeOffset? StartsAt);

/// <summary>
/// The whole of an event, as the screen that edits one needs it.
/// </summary>
/// <remarks>
/// Separate from <see cref="EventSummary"/> rather than a widening of it.
/// Every applicants and forms screen already carries a summary per event to
/// fill a dropdown, and none of them has any use for a capacity — a dropdown
/// is not improved by being told how many people fit in the room.
/// <para>
/// Every date here is an instant, and every one of them is nullable, because
/// none of them is decided on the day an event is created.
/// </para>
/// </remarks>
public sealed record EventDetail(
    Guid Id,
    string Slug,
    string Name,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    DateTimeOffset? RegistrationOpensAt,
    DateTimeOffset? RegistrationClosesAt,
    DateTimeOffset? DecisionsAnnouncedAt,
    int? Capacity,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy);

/// <summary>
/// A field an update either carries or leaves alone.
/// </summary>
/// <remarks>
/// The distinction a plain nullable cannot make. An update naming only
/// <c>registrationOpensAt</c> must not read as "and clear the other four", and
/// an update sending <c>"endsAt": null</c> has to mean something — deciding a
/// date and then un-deciding it is a normal week here. Absent and null are
/// different requests, so they are different values.
/// <para>
/// <c>default</c> is absent, so an edit only has to name what it changes.
/// </para>
/// </remarks>
public readonly record struct Patch<T>(bool Present, T? Value) where T : struct
{
    /// <summary>Sets the field, to a value or to nothing.</summary>
    public static Patch<T> To(T? value) => new(true, value);
}

/// <summary>
/// The parts of an event that can change after it exists.
/// </summary>
/// <remarks>
/// The slug is deliberately not here. It is the identifier that ends up in
/// links, in whatever somebody bookmarked, and in the support message that
/// arrives a year later, and a renamed identifier is a broken one. The name is
/// the part meant to be corrected.
/// <para>
/// <see cref="Name"/> is a plain nullable rather than a
/// <see cref="Patch{T}"/> because the column is NOT NULL: there is no clearing
/// it, only leaving it alone.
/// </para>
/// </remarks>
public sealed record EventEdit
{
    public string? Name { get; init; }
    public Patch<DateTimeOffset> StartsAt { get; init; }
    public Patch<DateTimeOffset> EndsAt { get; init; }
    public Patch<DateTimeOffset> RegistrationOpensAt { get; init; }
    public Patch<DateTimeOffset> RegistrationClosesAt { get; init; }
    public Patch<DateTimeOffset> DecisionsAnnouncedAt { get; init; }
    public Patch<int> Capacity { get; init; }
}

/// <summary>
/// The events everything else is scoped to.
/// </summary>
/// <remarks>
/// There is one event a year and it used to be made by hand, in psql. That is
/// the whole reason staging had none while a developer's laptop did, and why
/// the only thing this interface could do was list what somebody else had
/// already inserted.
/// <para>
/// Nothing here deletes. An event with applications attached cannot be deleted
/// in any sense a person would recognise — what hangs off it is several
/// hundred people's answers, a form somebody published, and the frozen
/// recipient list of every campaign already sent — and nothing else in this
/// system deletes either. An event made by mistake is renamed, or left undated
/// and ignored.
/// </para>
/// </remarks>
public interface IEventStore
{
    /// <summary>Newest first, so the one being run now is first.</summary>
    Task<IReadOnlyList<EventSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>The same order, with everything an editor needs.</summary>
    Task<IReadOnlyList<EventDetail>> ListDetailedAsync(CancellationToken ct = default);

    /// <summary>One event, or null when there is no such id.</summary>
    Task<EventDetail?> ByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Makes an event. A slug, a name, and nothing else.
    /// </summary>
    /// <remarks>
    /// Everything else is a date nobody has agreed yet. Demanding them at
    /// creation would mean either inventing them or not creating the event,
    /// and not creating the event is how a season starts with no root to hang
    /// a form off.
    /// </remarks>
    /// <exception cref="Npgsql.PostgresException">
    /// SQLSTATE 23505 when the slug is taken. Left to the caller because the
    /// answer is a sentence about that slug rather than a fault.
    /// </exception>
    Task<EventDetail> CreateAsync(
        string slug, string name, Guid createdBy, CancellationToken ct = default);

    /// <summary>Applies an edit, or returns null when there is no such id.</summary>
    Task<EventDetail?> UpdateAsync(Guid id, EventEdit edit, CancellationToken ct = default);
}
