using System.Globalization;
using System.Net.Mail;
using System.Text.Json;

namespace MorganHacks.Applications.Forms;

/// <summary>
/// Whether what arrived is an answer to the form that was actually asked.
/// </summary>
/// <remarks>
/// The page checks the same things before it posts, and that check is a
/// courtesy — it saves a round trip and puts the message next to the box. This
/// one is the real one. Nothing stops somebody posting to the endpoint
/// directly, so any rule that only exists in the browser is a rule that does
/// not exist.
/// <para>
/// The fields passed in must be the published version loaded here, never a
/// list the request supplied. Validating against a list the caller sent is
/// validating a claim against itself: "this question was optional" would be
/// true whenever the caller said so.
/// </para>
/// </remarks>
public static class SubmissionValidation
{
    internal const int MaxFilenameLength = 255;

    /// <summary>
    /// Every problem, not just the first.
    /// </summary>
    /// <remarks>
    /// This is a phone form somebody is filling in on a bus. Sending them back
    /// one complaint at a time turns a single mistake into six round trips.
    /// </remarks>
    public static IReadOnlyList<FormProblem> Check(
        IReadOnlyList<FormField> fields,
        IReadOnlyDictionary<string, JsonElement> answers)
    {
        var problems = new List<FormProblem>();

        foreach (var field in fields)
        {
            // Anything the form does not ask about is ignored rather than
            // rejected. An extra key is not something an applicant can fix,
            // and a form that shrinks between two tabs being open would
            // otherwise refuse a submission for a question it no longer has.
            var given = answers.TryGetValue(field.Key, out var value) ? value : default;

            if (!IsAnswered(field, given))
            {
                if (field.Required)
                {
                    problems.Add(new FormProblem(
                        field.Type == FieldType.Consent
                            ? $"You have to agree to \"{Shorten(field.Label)}\" to continue."
                            : $"\"{Shorten(field.Label)}\" is required.",
                        field.Key));
                }

                // Nothing further to say about an answer that is not there.
                continue;
            }

            Inspect(field, given, problems);
        }

        return problems;
    }

    private static void Inspect(FormField field, JsonElement value, List<FormProblem> problems)
    {
        switch (field.Type)
        {
            case FieldType.ShortText:
            case FieldType.Paragraph:
                CheckLength(field, Text(value), problems);
                break;

            case FieldType.Email:
                var address = Text(value);
                CheckLength(field, address, problems);

                // Parsed rather than pattern-matched. Every regex anybody
                // writes for this rejects somebody's real address, and for the
                // application form that address is the only way to reach them.
                if (!MailAddress.TryCreate(address, out _))
                {
                    problems.Add(new FormProblem(
                        $"\"{Shorten(field.Label)}\" does not look like an email address.",
                        field.Key));
                }

                break;

            case FieldType.Phone:
                // Length only. Numbers arrive with country codes, spaces,
                // brackets and extensions, and a format check here would
                // reject a real number that somebody then cannot be called on.
                CheckLength(field, Text(value), problems);
                break;

            case FieldType.Number:
                if (!TryNumber(value, out var number))
                {
                    problems.Add(new FormProblem(
                        $"\"{Shorten(field.Label)}\" has to be a number.", field.Key));
                    break;
                }

                if (field.Min is { } min && number < min)
                {
                    problems.Add(new FormProblem(
                        $"\"{Shorten(field.Label)}\" cannot be below {min}.", field.Key));
                }

                if (field.Max is { } max && number > max)
                {
                    problems.Add(new FormProblem(
                        $"\"{Shorten(field.Label)}\" cannot be above {max}.", field.Key));
                }

                // An age of 20.5 fits the form and not the column. Caught here
                // so the applicant is told, rather than at the INSERT where
                // the only available answer is a 500.
                if (field.Storage == AnswerStorage.Column
                    && AnswerColumns.TryKindOf(field.Column, out var kind)
                    && kind == ColumnKind.Integer
                    && (number != decimal.Truncate(number)
                        || number is < int.MinValue or > int.MaxValue))
                {
                    problems.Add(new FormProblem(
                        $"\"{Shorten(field.Label)}\" has to be a whole number.", field.Key));
                }

                break;

            case FieldType.Date:
                if (!DateOnly.TryParseExact(
                        Text(value), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out _))
                {
                    problems.Add(new FormProblem(
                        $"\"{Shorten(field.Label)}\" has to be a date.", field.Key));
                }

                break;

            case FieldType.Select:
            case FieldType.Radio:
                CheckOption(field, Text(value), problems);
                break;

            case FieldType.Checkboxes:
                if (value.ValueKind != JsonValueKind.Array)
                {
                    problems.Add(new FormProblem(
                        $"\"{Shorten(field.Label)}\" has to be a list of choices.", field.Key));
                    break;
                }

                foreach (var chosen in value.EnumerateArray())
                {
                    CheckOption(field, Text(chosen), problems);
                }

                break;

            case FieldType.Consent:
                // Already known to be a tick: an unticked box is "not
                // answered", which is what the required check above reads.
                break;

            case FieldType.File:
                var filename = Filename(value);
                if (filename is null)
                {
                    problems.Add(new FormProblem(
                        $"\"{Shorten(field.Label)}\" needs a file.", field.Key));
                }
                else if (filename.Length > MaxFilenameLength)
                {
                    // resume_filename is a plain text column with nothing
                    // bounding it, and this endpoint takes no authentication.
                    problems.Add(new FormProblem(
                        "That file's name is too long. Rename it and try again.", field.Key));
                }

                break;
        }
    }

    /// <summary>
    /// Refuses an answer that is not one of the options offered.
    /// </summary>
    /// <remarks>
    /// The value is what gets stored and later counted, so an unlisted one
    /// does not fail loudly — it turns up months later as a category on a
    /// report that nobody put there.
    /// </remarks>
    private static void CheckOption(FormField field, string value, List<FormProblem> problems)
    {
        if (!field.Options.Any(o => string.Equals(o.Value, value, StringComparison.Ordinal)))
        {
            problems.Add(new FormProblem(
                $"\"{Shorten(field.Label)}\" was answered with something that is not on the form.",
                field.Key));
        }
    }

    private static void CheckLength(FormField field, string value, List<FormProblem> problems)
    {
        if (field.MinLength is { } min && value.Length < min)
        {
            problems.Add(new FormProblem(
                $"\"{Shorten(field.Label)}\" needs at least {min} characters.", field.Key));
        }

        // A cap even when the form sets none. Without one this is a way to
        // push megabytes into a jsonb column from an unauthenticated endpoint,
        // one request at a time.
        var max = field.MaxLength ?? DefaultMaxLength(field.Type);
        if (value.Length > max)
        {
            problems.Add(new FormProblem(
                $"\"{Shorten(field.Label)}\" has to be under {max} characters.", field.Key));
        }
    }

    private static int DefaultMaxLength(FieldType type) =>
        type == FieldType.Paragraph ? 5_000 : 500;

    /// <summary>
    /// Whether there is an answer here at all.
    /// </summary>
    /// <remarks>
    /// Blank is absent, not present-and-empty. A required question left as an
    /// empty string is the same omission as one whose key never arrived, and
    /// treating them differently means an applicant sees "required" for one
    /// and silence for the other.
    /// </remarks>
    internal static bool IsAnswered(FormField field, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => false,

        // An unticked agreement is not an answer. It is the absence of one,
        // which is exactly what a required consent has to catch.
        JsonValueKind.False => false,
        JsonValueKind.True => true,

        JsonValueKind.Number => true,
        JsonValueKind.String => value.GetString() is { } text
            && (field.Type == FieldType.Consent
                ? bool.TryParse(text, out var ticked) && ticked
                : !string.IsNullOrWhiteSpace(text)),
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => Filename(value) is not null,
        _ => false,
    };

    /// <summary>The value as text, whatever JSON shape it arrived in.</summary>
    internal static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => string.Empty,
    };

    internal static bool TryNumber(JsonElement value, out decimal number)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDecimal(out number);
        }

        // A number input posts a string. Accepting one here is not laxity —
        // rejecting it would refuse a perfectly ordinary answer over how the
        // browser chose to encode it.
        return decimal.TryParse(
            Text(value), NumberStyles.Number, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>The name of the file an applicant picked, if they picked one.</summary>
    internal static string? Filename(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("name", out var name)
            || name.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var trimmed = name.GetString()?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Trims a label for a message.
    /// </summary>
    /// <remarks>
    /// MLH's data-sharing agreement is sixty words. Quoted whole above a text
    /// box it buries the actual complaint.
    /// </remarks>
    private static string Shorten(string label) =>
        label.Length <= 48 ? label : label[..45].TrimEnd() + "…";
}
