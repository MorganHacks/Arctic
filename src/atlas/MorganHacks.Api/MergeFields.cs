using System.Globalization;
using System.Text;
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
/// The list itself is not written here. It is
/// <see cref="ApplicantColumns.Mergeable"/> — the columns
/// <c>applications.applications</c> actually has — so a placeholder exists
/// because a column does, and a name nobody can fill cannot be offered because
/// there is nothing to derive it from. What this file adds is the two things
/// that are the mail's business rather than the table's: what a column is
/// called in a template, and how its value reads in a sentence.
/// </para>
/// <para>
/// Derived from a declared list rather than from the live schema. Reading
/// <c>information_schema</c> on every request would be a query on the editor's
/// keystroke and, worse, would let a migration change this API without anybody
/// deciding to. The declaration is checked against the schema by a test
/// instead, so the two cannot drift and the failure lands in CI rather than in
/// a send.
/// </para>
/// </remarks>
public static class MergeFields
{
    /// <summary>
    /// One placeholder, and the column behind it.
    /// </summary>
    /// <remarks>
    /// <see cref="Name"/> is derived rather than declared — see
    /// <see cref="NameFor"/> — so there is no second spelling of a column to
    /// keep in step with the first.
    /// </remarks>
    public sealed record MergeField(string Name, ApplicantColumn Column)
    {
        /// <summary>What the editor shows beside the name.</summary>
        public string Description => Column.Description;

        /// <summary>Whether a typed list of addresses can fill this.</summary>
        public bool OnAddressLists => Column.OnAddressLists;
    }

    /// <summary>Every placeholder that resolves, in the order an editor lists them.</summary>
    public static readonly IReadOnlyList<MergeField> All =
        [.. ApplicantColumns.Mergeable.Select(
            column => new MergeField(NameFor(column.Column), column))];

    /// <summary>
    /// The one place a column name becomes a placeholder name.
    /// </summary>
    /// <remarks>
    /// <c>first_name</c> is <c>{{firstName}}</c> because this says so, and
    /// there is nowhere else that could say otherwise. A declared name beside
    /// each column would be a second thing to get right, and the way it goes
    /// wrong is a placeholder the editor offers under one spelling and the
    /// send looks up under another.
    /// </remarks>
    public static string NameFor(string column)
    {
        var parts = column.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var name = new StringBuilder(parts[0]);

        foreach (var part in parts.Skip(1))
        {
            name.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
        }

        return name.ToString();
    }

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
            if (member.Fields.TryGetValue(field.Column.Column, out var stored)
                && Reads(stored, field.Column) is { } value)
            {
                values[field.Name] = value;
            }
        }

        return values;
    }

    /// <summary>
    /// How one column's value reads inside a sentence, or null for nothing.
    /// </summary>
    /// <remarks>
    /// Per type, because the defaults are wrong in ways somebody only notices
    /// after four hundred copies have left: a <c>boolean</c> stringifies as
    /// "True", which is a word no email has ever contained, and a year through
    /// a thousands separator is "2,027".
    /// <para>
    /// Null is the case that matters, and it covers three things that are the
    /// same thing to a reader: the column is null, the column is text nobody
    /// typed into, or the segment did not carry the column at all. All of them
    /// return null, which keeps the value out of the dictionary, which is what
    /// <see cref="Unfilled"/> counts and what the preview reports before
    /// anybody can send it.
    /// </para>
    /// <para>
    /// The throw is unreachable while the divergence test passes: it fires
    /// only if a column's declared kind stops matching the type the table
    /// hands back, and that test fails first, in CI, with the column named.
    /// Throwing rather than rendering something is deliberate all the same —
    /// the alternative is guessing at a value that goes out under our name.
    /// </para>
    /// </remarks>
    private static string? Reads(object? stored, ApplicantColumn column) =>
        (column.Kind, stored) switch
        {
            (_, null) => null,
            (ColumnKind.Text, string text) => string.IsNullOrWhiteSpace(text) ? null : text,
            (ColumnKind.Integer, int number) => number.ToString(CultureInfo.InvariantCulture),
            (ColumnKind.Boolean, bool yes) => yes ? "yes" : "no",
            _ => throw new InvalidOperationException(
                $"applications.applications.{column.Column} is declared "
                + $"{column.Kind} and came back as "
                + $"{stored.GetType().Name}."),
        };

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
