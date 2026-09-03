using MorganHacks.Applications.Segments;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Api;

/// <summary>
/// The <c>{{placeholders}}</c> a broadcast can fill in, and what fills them.
/// </summary>
/// <remarks>
/// One list, read four ways. <see cref="Values"/> builds what
/// <see cref="TemplateRenderer"/> is handed for one recipient;
/// <see cref="Fillable"/> says which of them a segment carries for everybody;
/// <see cref="Unfilled"/> says which of them one person has nothing for; and
/// the two placeholder endpoints hand the names to the template editor so it
/// can offer them.
/// <para>
/// One list rather than four, because the failure of four is silent. An editor
/// offering a placeholder the renderer has never heard of produces a template
/// that reads fine, passes review, and refuses at send — so the person who
/// wrote it finds out from the approver who could not send it, which is the
/// one place in this system where being told late costs somebody else's
/// afternoon.
/// </para>
/// <para>
/// Three, and no more. Every field here is one a template author can rely on
/// for every recipient of every segment, which is the property that makes a
/// refusal possible before the send rather than after it. Growing this list is
/// cheap; growing it by something only some segments carry is how
/// "Hi {{school}}," reaches four hundred people.
/// </para>
/// </remarks>
public static class MergeFields
{
    /// <summary>
    /// One placeholder, and where its value comes from.
    /// </summary>
    /// <remarks>
    /// <see cref="Read"/> is a function rather than a name callers switch on,
    /// so adding a field is adding a row here and changing nothing else.
    /// <para>
    /// <see cref="OnAddressLists"/> is what a typed list of addresses can
    /// supply, which is an address and nothing else — the recipient there is
    /// frequently a sponsor contact this system has never heard of, so a
    /// template that greets people by name cannot be sent to one.
    /// </para>
    /// </remarks>
    public sealed record MergeField(
        string Name,
        string Description,
        Func<SegmentMember, string?> Read,
        bool OnAddressLists);

    /// <summary>Every placeholder that resolves, in the order an editor lists them.</summary>
    public static readonly IReadOnlyList<MergeField> All =
    [
        new("email",
            "The address this message is going to.",
            member => member.Email,
            OnAddressLists: true),

        new("firstName",
            "The recipient's first name, as their application has it.",
            member => member.FirstName,
            OnAddressLists: false),

        new("lastName",
            "The recipient's last name, as their application has it.",
            member => member.LastName,
            OnAddressLists: false),
    ];

    /// <summary>
    /// The merge values a segment can supply for one recipient.
    /// </summary>
    /// <remarks>
    /// A field with nothing behind it is left out rather than written in
    /// empty, so <see cref="TemplateRenderer"/> leaves the placeholder
    /// standing and <see cref="Unfilled"/> can see that it did. That is what
    /// makes the gap countable: a row autosaved before somebody typed their
    /// name has an email and no first name, and the template greeting them
    /// would reach them as "Hi {{firstName}},".
    /// </remarks>
    public static Dictionary<string, string> Values(SegmentMember member)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in All)
        {
            if (field.Read(member) is { } value && !string.IsNullOrWhiteSpace(value))
            {
                values[field.Name] = value;
            }
        }

        return values;
    }

    /// <summary>The placeholders a segment can fill for everybody in it.</summary>
    public static IReadOnlySet<string> Fillable(Segment segment) =>
        new HashSet<string>(For(segment).Select(field => field.Name), StringComparer.Ordinal);

    /// <summary>The fields a segment can fill, described for the editor.</summary>
    /// <remarks>
    /// Narrowed rather than annotated. A list that offered <c>{{firstName}}</c>
    /// beside a note saying this segment cannot fill it is a list somebody
    /// clicks anyway.
    /// </remarks>
    public static IEnumerable<MergeField> For(Segment segment) =>
        segment is Segment.Addresses ? All.Where(field => field.OnAddressLists) : All;

    /// <summary>
    /// Which of the placeholders a template asks for this recipient has
    /// nothing to fill.
    /// </summary>
    /// <remarks>
    /// Sorted, because these are read out on a screen and by an assertion, and
    /// both want the same order twice.
    /// </remarks>
    public static IReadOnlyList<string> Unfilled(
        IReadOnlySet<string> wanted, SegmentMember member)
    {
        var values = Values(member);

        return wanted.Where(placeholder => !values.ContainsKey(placeholder))
                     .OrderBy(placeholder => placeholder, StringComparer.Ordinal)
                     .ToList();
    }
}
