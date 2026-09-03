namespace MorganHacks.Applications.Forms;

/// <summary>Something wrong with a form, in words its author can act on.</summary>
public sealed record FormProblem(string Message, string? FieldKey = null);

/// <summary>
/// Whether a form is safe to put in front of applicants.
/// </summary>
/// <remarks>
/// Checked at publish rather than at every edit. A half-finished draft is a
/// normal thing to have — somebody is in the middle of writing it — and
/// complaining while they type is how a tool becomes annoying enough to work
/// around.
/// <para>
/// Publishing is the moment the form becomes something several hundred people
/// will answer, and after which it can never be corrected for the ones who
/// already did. That is the moment worth being strict.
/// </para>
/// </remarks>
public static class FormValidation
{
    /// <summary>Every problem, not just the first.</summary>
    /// <remarks>
    /// One at a time turns fixing a form into a guessing game where each fix
    /// reveals the next complaint.
    /// </remarks>
    public static IReadOnlyList<FormProblem> Check(IReadOnlyList<FormField> fields)
    {
        var problems = new List<FormProblem>();

        var duplicates = fields
            .GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var duplicate in duplicates)
        {
            // The second silently overwrites the first's answer, and nothing
            // about the form looks wrong while it happens.
            problems.Add(new FormProblem(
                $"Two questions share the key '{duplicate.Key}'. Each needs its own.",
                duplicate.Key));
        }

        // The same failure as a duplicate key, one level down. Two questions
        // can hold different keys and still be pointed at one column, and then
        // the second answer lands on top of the first with nothing to show for
        // it — not in the form, not in the export, not until somebody notices a
        // person's answer is somebody else's.
        var collisions = fields
            .Where(f => f.Storage == AnswerStorage.Column
                && !string.IsNullOrWhiteSpace(f.Column))
            .GroupBy(f => f.Column!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var collision in collisions)
        {
            foreach (var field in collision)
            {
                problems.Add(new FormProblem(
                    $"\"{Shorten(field.Label)}\" stores in the column "
                    + $"'{collision.Key}', and so does another question. "
                    + "One of them has to move.",
                    field.Key));
            }
        }

        foreach (var field in fields)
        {
            var section = field.Type == FieldType.Section;

            if (string.IsNullOrWhiteSpace(field.Label))
            {
                problems.Add(new FormProblem(
                    section
                        ? "This page break has no heading."
                        : "This question has no wording.",
                    field.Key));
            }

            if (section)
            {
                // Everything that describes an answer is meaningless on a page
                // break, and refused rather than ignored. Silently dropping
                // what an author set is how a form ends up not doing what the
                // screen said it would, and the one that matters most is
                // Required: a section can never be answered, so a required one
                // is a question nobody can satisfy. Caught here, it is a
                // sentence at publish; ignored, it is either a form that
                // refuses every submission or a rule that only appears to
                // exist.
                if (field.Required)
                {
                    problems.Add(new FormProblem(
                        $"\"{Shorten(field.Label)}\" is a page break, so it cannot be required.",
                        field.Key));
                }

                if (field.Options.Count > 0)
                {
                    problems.Add(new FormProblem(
                        $"\"{Shorten(field.Label)}\" is a page break, so it cannot have options.",
                        field.Key));
                }

                if (field.Storage == AnswerStorage.Column)
                {
                    problems.Add(new FormProblem(
                        $"\"{Shorten(field.Label)}\" is a page break, so it has no answer to "
                        + "store anywhere.",
                        field.Key));
                }

                // Nothing below this applies to something nobody answers.
                continue;
            }

            var needsOptions = field.Type
                is FieldType.Select or FieldType.Radio or FieldType.Checkboxes;

            if (needsOptions && field.Options.Count == 0)
            {
                problems.Add(new FormProblem(
                    $"\"{Shorten(field.Label)}\" is a choice question with nothing to choose from.",
                    field.Key));
            }

            if (needsOptions)
            {
                var repeated = field.Options
                    .GroupBy(o => o.Value, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key);

                foreach (var value in repeated)
                {
                    // Distinct labels, one stored value: the answers are
                    // indistinguishable afterwards and no reporting recovers
                    // which was meant.
                    problems.Add(new FormProblem(
                        $"\"{Shorten(field.Label)}\" has two options stored as '{value}'.",
                        field.Key));
                }
            }

            if (field.Storage == AnswerStorage.Column && string.IsNullOrWhiteSpace(field.Column))
            {
                problems.Add(new FormProblem(
                    $"\"{Shorten(field.Label)}\" is set to store in a column but names none.",
                    field.Key));
            }

            // A consent question that is not required is an opt-in, which is
            // fine. A consent question with options is a misunderstanding.
            if (field.Type == FieldType.Consent && field.Options.Count > 0)
            {
                problems.Add(new FormProblem(
                    $"\"{Shorten(field.Label)}\" is an agreement, so it cannot have options.",
                    field.Key));
            }
        }

        // A form has to ask something. Page breaks are headings, so a form made
        // only of them — or of nothing at all — publishes a link that collects
        // nothing, and on an application form there is not even an address to
        // create an applicant from. It reads as working right up until somebody
        // asks where the answers went.
        if (!fields.Any(f => f.Type != FieldType.Section))
        {
            problems.Add(new FormProblem("This form does not ask anything yet."));
        }

        // One resume, or the upload has nowhere to go: an application holds a
        // single resume_key.
        var files = fields.Count(f => f.Type == FieldType.File);
        if (files > 1)
        {
            problems.Add(new FormProblem(
                $"There are {files} file questions. An application stores one file."));
        }

        return problems;
    }

    public static bool CanPublish(IReadOnlyList<FormField> fields) =>
        Check(fields).Count == 0;

    /// <summary>
    /// Trims a label for an error message.
    /// </summary>
    /// <remarks>
    /// The data-sharing agreement a new application form starts with is sixty
    /// words. Quoted whole in an error it buries the actual complaint.
    /// </remarks>
    private static string Shorten(string label) =>
        label.Length <= 48 ? label : label[..45].TrimEnd() + "…";
}
