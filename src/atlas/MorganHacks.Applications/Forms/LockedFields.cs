using System.Text.RegularExpressions;

namespace MorganHacks.Applications.Forms;

/// <summary>What a save is allowed to do, and what it quietly cannot.</summary>
/// <param name="Fields">The fields to write, or null when the save is refused.</param>
/// <param name="Problems">Why it was refused. Empty when it was not.</param>
public readonly record struct ReconciledDraft(
    IReadOnlyList<FormField>? Fields,
    IReadOnlyList<FormProblem> Problems);

/// <summary>
/// The half of the locking rule that a browser cannot be trusted with.
/// </summary>
/// <remarks>
/// The builder disables the controls on MLH's questions, and that is worth
/// doing, but a disabled input is a suggestion. Anybody can send the draft
/// endpoint whatever array they like, and the obligation these questions carry
/// is not one we get to lose to a curl command or a stale tab running last
/// week's JavaScript.
/// <para>
/// Two different answers, because the two mistakes are different. Removing a
/// locked question is refused outright — the builder never permits it, so a
/// request that asks for it is either tampering or a client bug, and silently
/// putting the question back would hide both. Rewording, retyping or
/// unlocking one is instead normalised away: it is a no-op for an honest
/// client, so refusing would turn a harmless round-trip difference into a
/// failed autosave in the middle of somebody's sentence.
/// </para>
/// </remarks>
public static partial class LockedFields
{
    /// <summary>MLH's questions, exactly as they must appear, by key.</summary>
    private static readonly Dictionary<string, FormField> Canonical =
        MlhFields.All.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

    public static bool IsLocked(string key) => Canonical.ContainsKey(key);

    /// <summary>The keys the builder must render as uneditable.</summary>
    public static IReadOnlySet<string> Keys { get; } =
        MlhFields.All.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A key that survives being a JSON property, a CSV header and a column
    /// name, because it eventually has to be all three.
    /// </summary>
    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex KeyShape { get; }

    /// <summary>
    /// Checks a submitted draft and returns what should actually be stored.
    /// </summary>
    /// <remarks>
    /// Order is taken from the submission, so reordering a locked question
    /// among the others is allowed — where it sits on the page is a
    /// presentation choice and none of MLH's business. Everything else about
    /// it comes from <see cref="MlhFields"/>.
    /// </remarks>
    public static ReconciledDraft Reconcile(
        IReadOnlyList<FormField> submitted, bool requiresMlh)
    {
        var problems = new List<FormProblem>();

        foreach (var field in submitted)
        {
            // Refused rather than repaired. A key generated here would be a
            // different one on every autosave, and the key is what an answer
            // is filed under — the one property in this whole document that
            // must never change by itself.
            if (!KeyShape.IsMatch(field.Key ?? string.Empty))
            {
                problems.Add(new FormProblem(
                    $"A question has an unusable key '{field.Key}'. Keys are lower case "
                    + "letters, digits and underscores, and are set when the question is "
                    + "added.",
                    field.Key));
            }
        }

        // Only the application form carries MLH's questions. They are about
        // people coming to the event, and a mentor sign-up or a feedback survey
        // is not that — demanding a code of conduct agreement and a level of
        // study on every form makes the builder useless for anything else.
        if (requiresMlh)
        {
            var present = submitted
                .Select(f => f.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, locked) in Canonical.Where(c => !present.Contains(c.Key)))
            {
                problems.Add(new FormProblem(
                    $"\"{Shorten(locked.Label)}\" is required by MLH and cannot be removed.",
                    key));
            }
        }

        if (problems.Count > 0)
        {
            return new ReconciledDraft(null, problems);
        }

        var fields = submitted
            .Select(field => requiresMlh && Canonical.TryGetValue(field.Key, out var locked)
                ? locked
                // Locked is not a flag a client gets to set. Left alone, an
                // author could mark their own question undeletable and nobody
                // — including them — would be able to take it off again.
                : field with { Locked = false })
            .ToList();

        return new ReconciledDraft(fields, []);
    }

    /// <summary>Trims a label for an error message.</summary>
    /// <remarks>
    /// The data-sharing agreement is sixty words. Quoted whole it buries the
    /// complaint it is attached to.
    /// </remarks>
    private static string Shorten(string label) =>
        label.Length <= 48 ? label : label[..45].TrimEnd() + "…";
}
