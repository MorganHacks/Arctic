namespace MorganHacks.Applications.Forms;

/// <summary>
/// The kinds of question the form can ask, and the one thing on it that is not
/// a question.
/// </summary>
/// <remarks>
/// Deliberately short. Every type here has to be rendered, validated, stored,
/// exported and shown back to a reviewer, so each one is real work — and a
/// question type nobody uses is work spent on nothing.
/// <para>
/// What is missing is missing on purpose: no payment, no signature, no matrix,
/// no ranking. Those are what make a form builder a product rather than a way
/// to register hackers.
/// </para>
/// </remarks>
public enum FieldType
{
    ShortText,
    Paragraph,
    Email,
    Phone,
    Number,
    Date,

    /// <summary>One of several, shown as a dropdown.</summary>
    Select,

    /// <summary>One of several, all visible. Better under about six options.</summary>
    Radio,

    /// <summary>Any number of several.</summary>
    Checkboxes,

    /// <summary>A single box that must be ticked. Agreements are these.</summary>
    Consent,

    /// <summary>A resume. One per application.</summary>
    File,

    /// <summary>
    /// A page break, and the heading of the page it opens.
    /// </summary>
    /// <remarks>
    /// The one member here that is not a question. Fifteen questions on one
    /// page is a wall somebody abandons halfway down, and the fix is the one
    /// Google Forms settled on: the author inserts a section, and everything
    /// after it is the next page. Everything before the first one is page one,
    /// so a form with no sections is the single page it has always been.
    /// <para>
    /// <see cref="FormField.Label"/> is that page's heading and
    /// <see cref="FormField.Help"/> is its description. It is never answered
    /// and no value is ever stored under its key.
    /// </para>
    /// <para>
    /// A field in the same array as the questions rather than a separate list
    /// of pages, because a page break is a thing an author drags around
    /// between questions. A parallel structure would need its own ordering
    /// alongside the fields' — and two orderings that have to agree are two
    /// orderings that eventually do not. Storage, versioning and ordering are
    /// untouched by this existing.
    /// </para>
    /// </remarks>
    Section,
}

/// <summary>One option in a select, radio or checkbox question.</summary>
/// <remarks>
/// The value is stored and the label is shown. They are separate so that
/// rewording an option later does not silently change what past applicants
/// appear to have answered.
/// </remarks>
public sealed record FieldOption(string Value, string Label);

/// <summary>
/// Where a question's answer is kept.
/// </summary>
/// <remarks>
/// The distinction that keeps the applications table useful. An answer that
/// gets filtered, exported, or read at check-in earns a real column; everything
/// else lives in one JSON blob rather than growing the table by a column per
/// question per year.
/// </remarks>
public enum AnswerStorage
{
    /// <summary>A named column on applications.applications.</summary>
    Column,

    /// <summary>A key in the responses jsonb.</summary>
    Responses,
}

/// <summary>
/// One question on the form, or the break between two of its pages.
/// </summary>
/// <remarks>
/// One record for both, because a <see cref="FieldType.Section"/> has to be
/// reordered, edited and versioned exactly like a question is. Splitting them
/// would mean two shapes, two orderings and two round trips through the
/// builder for one array the author sees as a single list.
/// <para>
/// The properties that describe an answer — <see cref="Required"/>,
/// <see cref="Options"/>, <see cref="Storage"/> and the bounds — are
/// meaningless on a section, which is why <see cref="FormValidation"/> refuses
/// to publish one that sets them rather than quietly ignoring what was set.
/// </para>
/// </remarks>
public sealed record FormField
{
    /// <summary>
    /// Stable across edits and versions.
    /// </summary>
    /// <remarks>
    /// This is the key an answer is stored under, so it must survive a question
    /// being reworded or moved. Generated once when the question is added and
    /// never regenerated — renaming it would orphan every answer already given.
    /// </remarks>
    public required string Key { get; init; }

    public required FieldType Type { get; init; }

    /// <summary>
    /// The question as the applicant reads it, or a page's heading when this
    /// is a <see cref="FieldType.Section"/>.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Shown under the question. For the thing people always ask — or, on a
    /// section, the description of the page it opens.
    /// </summary>
    public string? Help { get; init; }

    public bool Required { get; init; }

    public IReadOnlyList<FieldOption> Options { get; init; } = [];

    public AnswerStorage Storage { get; init; } = AnswerStorage.Responses;

    /// <summary>Which column, when this answer is stored as one.</summary>
    public string? Column { get; init; }

    /// <summary>
    /// Whether the registration team may remove or reword this question.
    /// </summary>
    /// <remarks>
    /// MLH affiliation mandates eight fields and two agreements. Locking them in
    /// the builder is how that obligation survives a well-meaning tidy-up the
    /// week before launch — the alternative is finding out at the export, when
    /// it is far too late to ask anybody again.
    /// </remarks>
    public bool Locked { get; init; }

    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
}
