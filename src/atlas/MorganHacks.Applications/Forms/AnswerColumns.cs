namespace MorganHacks.Applications.Forms;

/// <summary>How a column on <c>applications.applications</c> holds an answer.</summary>
public enum ColumnKind
{
    Text,
    Integer,
    Boolean,

    /// <summary>
    /// A moment rather than a flag. Ticking one of these stores when it was
    /// ticked.
    /// </summary>
    Timestamp,
}

/// <summary>
/// The columns an answer is allowed to land in.
/// </summary>
/// <remarks>
/// An allow-list rather than trust. <see cref="FormField.Column"/> is text an
/// organizer typed into the builder, and a column name cannot be a bound
/// parameter — it has to be written into the statement — so the only safe way
/// to use one is to have matched it against a list that lives here. A form
/// naming <c>status</c> would otherwise decide its own application had been
/// accepted.
/// <para>
/// The kind is the other half of the job. <c>mlh_coc_agreed_at</c> is a
/// timestamptz and <c>mlh_marketing_opt_in</c> is a boolean, and both are a
/// tick in the same checkbox on the page. What a tick means in the database
/// has to be looked up rather than inferred from what arrived.
/// </para>
/// <para>
/// Deliberately absent: <c>status</c>, <c>event_id</c>, <c>person_id</c>,
/// <c>form_version</c> and every lifecycle timestamp. Those are the
/// submission's own to set, not an answer's. <c>resume_key</c> is absent too —
/// a storage key is something we mint, never something a form hands us.
/// </para>
/// </remarks>
public static class AnswerColumns
{
    /// <summary>Where the applicant's address has to land.</summary>
    /// <remarks>
    /// <c>applications.email</c> is NOT NULL and the dedupe index is built on
    /// it, so a form with no question routed here cannot create an applicant
    /// at all.
    /// </remarks>
    public const string Email = "email";

    private static readonly Dictionary<string, ColumnKind> Writable =
        new(StringComparer.Ordinal)
        {
            [Email] = ColumnKind.Text,
            ["first_name"] = ColumnKind.Text,
            ["last_name"] = ColumnKind.Text,
            ["school"] = ColumnKind.Text,
            ["level_of_study"] = ColumnKind.Text,
            ["country"] = ColumnKind.Text,
            ["phone"] = ColumnKind.Text,
            ["shirt_size"] = ColumnKind.Text,
            ["dietary_needs"] = ColumnKind.Text,
            ["accessibility_needs"] = ColumnKind.Text,

            ["age"] = ColumnKind.Integer,
            ["graduation_year"] = ColumnKind.Integer,

            ["first_time_hacker"] = ColumnKind.Boolean,
            ["mlh_marketing_opt_in"] = ColumnKind.Boolean,

            ["mlh_coc_agreed_at"] = ColumnKind.Timestamp,
            ["mlh_data_sharing_at"] = ColumnKind.Timestamp,
        };

    public static bool TryKindOf(string? column, out ColumnKind kind)
    {
        kind = default;
        return column is not null && Writable.TryGetValue(column, out kind);
    }

    /// <summary>
    /// Whether this set of questions can produce an applicant at all.
    /// </summary>
    /// <remarks>
    /// Checked before a submission is attempted rather than left to the NOT
    /// NULL, because "null value in column email violates not-null
    /// constraint" is not something to show somebody who filled in a form.
    /// </remarks>
    public static bool AsksForAnAddress(IReadOnlyList<FormField> fields) =>
        fields.Any(f => f.Storage == AnswerStorage.Column && f.Column == Email);

}
