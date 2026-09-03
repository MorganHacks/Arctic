namespace MorganHacks.Applications.Forms;

/// <summary>
/// Every version of one form's questions, arranged for reading answers back.
/// </summary>
/// <remarks>
/// An answer is stored in one of two places and only one of them is
/// self-describing. The <c>responses</c> jsonb is keyed by
/// <see cref="FormField.Key"/> already, so it reads back on its own; a promoted
/// answer is a value in a column, and the only thing tying that column to a
/// question is the field list of the version the applicant answered.
/// <para>
/// That is why this holds every version rather than the published one. Reading
/// every response against the current questions is right until the first edit
/// and quietly wrong afterwards: a question moved from one column to another,
/// or rebuilt under a new key, would put somebody's school under a heading
/// they never answered. Each response carries its version number, and that
/// number picks the map.
/// </para>
/// </remarks>
public sealed class FormQuestions
{
    private static readonly IReadOnlyDictionary<string, string> Nothing =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> _byVersion;

    private FormQuestions(
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> byVersion,
        IReadOnlyList<string> columns,
        IReadOnlyList<FormField> published)
    {
        _byVersion = byVersion;
        Columns = columns;
        Published = published;
    }

    /// <summary>
    /// The columns a read has to select to find every promoted answer.
    /// </summary>
    /// <remarks>
    /// The union across every version, not just the published one, because a
    /// response given before a question was removed still has its answer in
    /// that column and dropping the column from the statement would lose it.
    /// <para>
    /// Every name here came back from <see cref="AnswerColumns.TryKindOf"/>,
    /// which is the same allow-list the write path checks against. A column
    /// name cannot be a bound parameter, so it has to be written into the
    /// statement — and having been matched against that list is the only thing
    /// that makes doing so safe. A form naming <c>status</c> gets no column
    /// here, and therefore no column in the SQL.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>
    /// The questions currently being asked. Empty when nothing is published.
    /// </summary>
    /// <remarks>
    /// What an export's columns are drawn from. Deliberately not the keys that
    /// happen to appear in the data: a question everybody skipped would
    /// otherwise vanish from the file, and an export whose shape depends on
    /// the answers is one no two runs agree on.
    /// </remarks>
    public IReadOnlyList<FormField> Published { get; }

    public static FormQuestions From(IEnumerable<FormVersion> versions)
    {
        var byVersion = new Dictionary<int, IReadOnlyDictionary<string, string>>();

        // Sorted so the statement this feeds is byte-for-byte the same on
        // every request, which is what lets Postgres reuse a plan for it.
        var columns = new SortedSet<string>(StringComparer.Ordinal);

        IReadOnlyList<FormField> published = [];

        foreach (var version in versions)
        {
            if (version.Status == "published")
            {
                published = version.Fields;
            }

            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in version.Fields)
            {
                if (field.Storage != AnswerStorage.Column
                    || !AnswerColumns.TryKindOf(field.Column, out _))
                {
                    // Either the answer lives in `responses`, or the question
                    // names a column nobody recognises — in which case the
                    // write path put the answer in `responses` too. Both read
                    // back from the jsonb without needing a mapping.
                    continue;
                }

                // First question wins if two point at one column. That is a
                // form the validator refuses to publish, and picking one
                // beats throwing on a read of data already written.
                keys.TryAdd(field.Column!, field.Key);
                columns.Add(field.Column!);
            }

            byVersion[version.Version] = keys;
        }

        return new FormQuestions(byVersion, [.. columns], published);
    }

    /// <summary>
    /// Which question each column held, for one version of the form.
    /// </summary>
    /// <remarks>
    /// Empty for a version that is not there — a row stamped with a version
    /// whose record has gone. Its promoted answers are dropped rather than
    /// guessed at, and whatever it put in <c>responses</c> still comes back,
    /// because that half never needed this map.
    /// </remarks>
    public IReadOnlyDictionary<string, string> KeysByColumn(int version) =>
        _byVersion.TryGetValue(version, out var keys) ? keys : Nothing;
}
