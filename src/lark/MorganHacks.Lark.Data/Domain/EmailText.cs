using System.Globalization;
using System.Net;
using System.Text;

namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// Reads an HTML body back as the plain-text part that goes beside it.
/// </summary>
/// <remarks>
/// <c>notify.templates</c> requires <c>body_text</c> and 0003 says why:
/// text-only clients exist, and a message with no text part scores worse with
/// spam filters. A template written in Markdown gets its text part from the
/// Markdown, which is already prose — see <see cref="TemplateMarkdown.ToText"/>.
/// A template written in HTML has no such source, so the text has to be read
/// out of the layout, and storing an empty string instead would cost
/// deliverability on every message the template ever sends.
/// <para>
/// <b>Readable, not faithful.</b> A table used for layout is not a table to
/// somebody reading text: its cells are run together on one line and its rows
/// are separated, because the alternative is a column of one-word lines that
/// reads as nothing at all. The two things worth preserving exactly are where
/// the paragraphs end and where the links went.
/// </para>
/// <para>
/// <b>Links keep their URL.</b> <c>text &lt;url&gt;</c>, which is the same
/// shape <see cref="TemplateMarkdown"/> writes, so the two text parts read
/// alike whichever language the template was written in. A link nobody can
/// follow is the one thing that makes a text part worthless.
/// </para>
/// <para>
/// <c>{{placeholders}}</c> are ordinary text to every rule here and come out
/// unchanged, which is what lets
/// <see cref="TemplateRenderer.PlaceholdersIn"/> still find them in the text
/// part after a template has been saved.
/// </para>
/// </remarks>
public static class EmailText
{
    /// <summary>
    /// Tags that end the line they are on.
    /// </summary>
    /// <remarks>
    /// Both opening and closing, so a paragraph is separated from its
    /// neighbours whichever end of it the walk is at. Cells are not here —
    /// they separate with a space, further down.
    /// </remarks>
    private static readonly HashSet<string> Blocks =
        new(StringComparer.Ordinal)
        {
            "p", "div", "center", "blockquote", "table", "thead", "tbody", "tfoot",
            "tr", "caption", "ul", "ol", "h1", "h2", "h3", "h4", "h5", "h6",
        };

    /// <summary>
    /// The plain-text reading of an HTML body.
    /// </summary>
    /// <remarks>
    /// Takes the sanitised HTML rather than what the author typed, so that the
    /// text part cannot contain something the HTML part was not allowed to —
    /// <see cref="EmailHtml.Discarded"/> is honoured here anyway, but reading
    /// the sanitised body means there is one answer to what is in this message
    /// rather than two that agree until somebody changes one of them.
    /// </remarks>
    public static string From(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var text = new StringBuilder(html.Length);
        var chunk = new StringBuilder();

        // Where each open <a> started in the output, and where it was pointing,
        // so the URL can be written after the words it was wrapped around.
        var links = new List<(string Href, int At)>();

        // One entry per open list: a number for <ol>, null for <ul>.
        var lists = new List<int?>();

        var i = 0;
        while (i < html.Length)
        {
            if (html[i] != '<')
            {
                chunk.Append(html[i]);
                i++;
                continue;
            }

            if (html.AsSpan(i).StartsWith("<!--", StringComparison.Ordinal))
            {
                Flush(text, chunk);
                var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3;
                continue;
            }

            if (i + 1 < html.Length && (html[i + 1] == '!' || html[i + 1] == '?'))
            {
                Flush(text, chunk);
                var end = html.IndexOf('>', i);
                i = end < 0 ? html.Length : end + 1;
                continue;
            }

            if (!EmailHtml.TryReadTag(html, i, out var tag, out var next))
            {
                // "a < b" is arithmetic, not markup.
                chunk.Append('<');
                i++;
                continue;
            }

            Flush(text, chunk);
            i = next;

            if (EmailHtml.Discarded.Contains(tag.Name))
            {
                // The body of a <script> is code. Printing it into a
                // plain-text email is printing it to the reader.
                if (!tag.Closing && !tag.SelfClosing)
                {
                    i = EmailHtml.SkipPast(html, tag.Name, i);
                }

                continue;
            }

            Write(tag, text, links, lists);
        }

        Flush(text, chunk);
        return Tidy(text.ToString());
    }

    /// <summary>What one tag does to the text being built.</summary>
    private static void Write(
        EmailHtml.Tag tag, StringBuilder text, List<(string Href, int At)> links,
        List<int?> lists)
    {
        switch (tag.Name)
        {
            case "br":
                text.Append('\n');
                return;

            case "hr":
                // The same rule TemplateMarkdown's text part draws, so the two
                // look alike in a client showing plain text.
                text.Append("\n---\n");
                return;

            case "img":
                // The alt text and nothing else. It is the only part of a
                // picture that survives having no pictures.
                text.Append(' ')
                    .Append(WebUtility.HtmlDecode(Attribute(tag, "alt")))
                    .Append(' ');
                return;

            case "a":
                Anchor(tag, text, links);
                return;

            case "ol":
            case "ul":
                if (tag.Closing)
                {
                    if (lists.Count > 0)
                    {
                        lists.RemoveAt(lists.Count - 1);
                    }
                }
                else if (!tag.SelfClosing)
                {
                    lists.Add(tag.Name == "ol" ? 1 : null);
                }

                text.Append('\n');
                return;

            case "li":
                text.Append('\n');
                if (!tag.Closing)
                {
                    text.Append(Marker(lists));
                }

                return;

            case "td":
            case "th":
                // A space rather than a newline. A layout table's row is one
                // line of prose; breaking on every cell turns a button and its
                // caption into a column of single words.
                text.Append(' ');
                return;

            default:
                if (Blocks.Contains(tag.Name))
                {
                    text.Append('\n');
                }

                return;
        }
    }

    /// <summary>
    /// Opens a link by remembering where it started, closes it by writing where
    /// it went.
    /// </summary>
    /// <remarks>
    /// The URL is left off when it is the same as the words, because
    /// "https://example.com &lt;https://example.com&gt;" is noise, and off
    /// entirely when the href is a bare <c>{{placeholder}}</c> that the link
    /// text already is.
    /// </remarks>
    private static void Anchor(
        EmailHtml.Tag tag, StringBuilder text, List<(string Href, int At)> links)
    {
        if (!tag.Closing)
        {
            if (!tag.SelfClosing)
            {
                links.Add((WebUtility.HtmlDecode(Attribute(tag, "href")).Trim(), text.Length));
            }

            return;
        }

        if (links.Count == 0)
        {
            return;
        }

        var (href, at) = links[^1];
        links.RemoveAt(links.Count - 1);

        if (href.Length == 0 || at > text.Length)
        {
            return;
        }

        var label = text.ToString(at, text.Length - at).Trim();
        if (label.Length == 0 || string.Equals(label, href, StringComparison.Ordinal))
        {
            return;
        }

        text.Append(" <").Append(href).Append('>');
    }

    /// <summary>The bullet or the number this item gets.</summary>
    private static string Marker(List<int?> lists)
    {
        if (lists.Count == 0)
        {
            return "- ";
        }

        var counter = lists[^1];
        if (counter is null)
        {
            return "- ";
        }

        lists[^1] = counter + 1;
        return string.Create(CultureInfo.InvariantCulture, $"{counter}. ");
    }

    private static string Attribute(EmailHtml.Tag tag, string name)
    {
        foreach (var (attribute, value) in tag.Attributes)
        {
            if (attribute == name)
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static void Flush(StringBuilder text, StringBuilder chunk)
    {
        if (chunk.Length == 0)
        {
            return;
        }

        // Decoded here rather than at the end, so that the words a link is
        // wrapped around are compared with its href as a reader would see
        // them, and so nothing further down has to know about entities.
        text.Append(WebUtility.HtmlDecode(chunk.ToString()));
        chunk.Clear();
    }

    /// <summary>
    /// Turns the walk's output into something somebody would read.
    /// </summary>
    /// <remarks>
    /// HTML's whitespace is meaningless and there is a great deal of it —
    /// indentation between the tags of a hand-written table is most of the
    /// characters in the file. Runs collapse to one space, a line is trimmed,
    /// and no more than one blank line survives between two blocks. Without
    /// this the text part of a builder's export is four hundred lines of
    /// nothing with six words in it.
    /// </remarks>
    private static string Tidy(string text)
    {
        var tidied = new StringBuilder(text.Length);
        var blanks = 0;

        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal)
                                .Replace('\r', '\n')
                                .Split('\n'))
        {
            var line = new StringBuilder(raw.Length);
            var space = false;

            foreach (var c in raw)
            {
                if (char.IsWhiteSpace(c))
                {
                    space = line.Length > 0;
                    continue;
                }

                if (space)
                {
                    line.Append(' ');
                    space = false;
                }

                line.Append(c);
            }

            if (line.Length == 0)
            {
                blanks++;
                continue;
            }

            if (tidied.Length > 0)
            {
                tidied.Append(blanks > 0 ? "\n\n" : "\n");
            }

            blanks = 0;
            tidied.Append(line);
        }

        return tidied.ToString();
    }
}
