using System.Text.RegularExpressions;

namespace MorganHacks.Applications.Forms;

/// <summary>
/// The one thing about a submitted draft that a browser cannot be trusted with.
/// </summary>
/// <remarks>
/// Everything else on a question is the author's to decide — the wording, the
/// type, whether it is there at all. The key is not, because it is not a
/// property of the question so much as the address every answer already given
/// is filed under. A key that changes shape between one autosave and the next
/// orphans answers, and nothing on screen looks wrong while it happens.
/// <para>
/// Refused rather than repaired. A key generated here would be a different one
/// on every save, so the only safe answer to an unusable key is to decline the
/// write and say which question carries it.
/// </para>
/// </remarks>
public static partial class DraftKeys
{
    /// <summary>
    /// A key that survives being a JSON property, a CSV header and a column
    /// name, because it eventually has to be all three.
    /// </summary>
    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex KeyShape { get; }

    /// <summary>
    /// Whatever is wrong with the keys in a submitted draft. Empty when
    /// nothing is.
    /// </summary>
    public static IReadOnlyList<FormProblem> Check(IReadOnlyList<FormField> submitted)
    {
        var problems = new List<FormProblem>();

        foreach (var field in submitted)
        {
            if (!KeyShape.IsMatch(field.Key ?? string.Empty))
            {
                problems.Add(new FormProblem(
                    $"A question has an unusable key '{field.Key}'. Keys are lower case "
                    + "letters, digits and underscores, and are set when the question is "
                    + "added.",
                    field.Key));
            }
        }

        return problems;
    }
}
