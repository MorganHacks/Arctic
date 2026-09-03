namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// The two languages a template's source may be written in, and the one place
/// that turns either of them into the two bodies a message needs.
/// </summary>
/// <remarks>
/// Markdown was the only one until authors needed a button, which needs a
/// table cell with a background colour, which Markdown cannot express. Rather
/// than grow the dialect into HTML one construct at a time, a template says
/// which language its source is in and is rendered accordingly.
/// <para>
/// Here rather than in the endpoint because both bodies have to come from the
/// source every time, in both languages, and a caller that renders one and
/// forgets the other writes a template that sends a blank half.
/// <see cref="EmailHtml.Sanitize"/> runs on the way out of both branches, so
/// there is no path to a stored <c>body_html</c> that has not been through the
/// allow-list.
/// </para>
/// </remarks>
public static class TemplateBody
{
    /// <summary>Source written in the small Markdown dialect.</summary>
    public const string Markdown = "markdown";

    /// <summary>Source written as HTML, as email has always been written.</summary>
    public const string Html = "html";

    /// <summary>Whether this is a language a template can be written in.</summary>
    public static bool IsFormat(string? format) =>
        format is Markdown or Html;

    /// <summary>
    /// The HTML and plain-text bodies for a source.
    /// </summary>
    /// <remarks>
    /// The text part is derived differently in each branch, on purpose.
    /// Markdown is already prose, so its text comes from the source with the
    /// markers taken off and every URL intact. HTML is not prose, so its text
    /// is read back out of the sanitised HTML by <see cref="EmailText"/> —
    /// from the sanitised body rather than from what was typed, so that the
    /// two parts of a message cannot say different things.
    /// </remarks>
    public static (string Html, string Text) Render(string format, string? source)
    {
        if (format == Html)
        {
            var html = EmailHtml.Sanitize(source);
            return (html, EmailText.From(html));
        }

        return (TemplateMarkdown.ToSafeHtml(source), TemplateMarkdown.ToText(source));
    }
}
