namespace MorganHacks.Applications.Segments;

/// <summary>
/// One person a segment resolved to.
/// </summary>
/// <remarks>
/// <see cref="PersonId"/> is null whenever the address is not one this system
/// knows — which is the normal case for <see cref="Segment.Addresses"/>, where
/// the recipient is a mentor or a sponsor rather than an applicant.
/// <para>
/// The names ride along because a template may greet somebody by name and
/// rendering happens once, at queue time, against the values that were true
/// then. Nothing else about the person comes out of here: a segment is a list
/// of who to mail, not a way to read an application.
/// </para>
/// </remarks>
public sealed record SegmentMember(
    Guid? PersonId, string Email, string? FirstName, string? LastName);

/// <summary>
/// What a segment resolves to, and whether it was too big to.
/// </summary>
/// <remarks>
/// <see cref="Overflowed"/> rather than a truncated list, because sending to
/// the first ten thousand of a segment somebody expected to be four hundred is
/// worse than refusing. The caller refuses.
/// </remarks>
public sealed record ResolvedSegment(IReadOnlyList<SegmentMember> Members, bool Overflowed);

/// <summary>
/// Turns a stored segment into the people it currently means.
/// </summary>
/// <remarks>
/// Lives in Applications because that is where <c>applications.*</c> lives and
/// a module owns its own tables. lark stores the segment document and never
/// looks inside it; this is the only thing that knows what is in there.
/// <para>
/// Deliberately re-run every time rather than cached. That is the whole reason
/// the resolved list is frozen into <c>notify.messages</c> at send: this
/// answers "who does that mean right now", and right now is a different set of
/// people every day of registration week.
/// </para>
/// </remarks>
public interface ISegmentResolver
{
    Task<ResolvedSegment> ResolveAsync(Segment segment, CancellationToken ct = default);
}
