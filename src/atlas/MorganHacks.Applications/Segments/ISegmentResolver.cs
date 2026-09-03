namespace MorganHacks.Applications.Segments;

/// <summary>
/// One person a segment resolved to.
/// </summary>
/// <remarks>
/// <see cref="PersonId"/> is null whenever the address is not one this system
/// knows — which is the normal case for <see cref="Segment.Addresses"/>, where
/// the recipient is a mentor or a sponsor rather than an applicant.
/// <para>
/// <see cref="Fields"/> is what a template may fill itself in from, keyed by
/// column name and holding the value as the column holds it — a
/// <see cref="string"/>, an <see cref="int"/> or a <see cref="bool"/>, or null
/// where the applicant left it blank. It rides along because rendering happens
/// once, at queue time, against the values that were true then. Deciding how
/// each of them reads is the mail's job and not this one's.
/// </para>
/// <para>
/// Still not a way to read an application: the only columns in here are the
/// ones <see cref="ApplicantColumns.Mergeable"/> names, and the resolver does
/// not select the others.
/// </para>
/// <para>
/// <see cref="Email"/> is also in <see cref="Fields"/>, and is a property of
/// its own because it is what the message is addressed to, deduped on and
/// suppressed by — none of which is a merge concern, and all of which would
/// otherwise be a dictionary lookup that can miss.
/// </para>
/// </remarks>
public sealed record SegmentMember(
    Guid? PersonId, string Email, IReadOnlyDictionary<string, object?> Fields);

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
