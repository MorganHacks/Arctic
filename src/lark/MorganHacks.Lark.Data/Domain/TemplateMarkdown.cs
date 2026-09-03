using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// Turns the Markdown an organizer types into the two bodies a message needs.
/// </summary>
/// <remarks>
/// <c>notify.templates</c> requires both <c>body_html</c> and <c>body_text</c>,
/// and 0003 says why: text-only clients exist, and a message with no text part
/// scores worse with spam filters. Both are generated here from one source, so
/// they cannot disagree — a hand-maintained text part is a text part that stops
/// matching the HTML the first week nobody remembers to update it.
/// <para>
/// A deliberately small dialect: paragraphs, headings, lists, links, bold,
/// italic, blockquotes, rules and images. It stays small now that
/// <see cref="EmailHtml"/> allows more than it produces, because the thing
/// Markdown is good for is prose. An author who needs a button needs a table
/// cell with a background colour, and the honest way to write one is to write
/// HTML and say so — see <see cref="TemplateBody"/> — rather than to grow this
/// dialect one layout construct at a time.
/// </para>
/// <para>
/// Raw HTML written inside a Markdown source still passes through to
/// <see cref="EmailHtml"/>, which is how a Markdown template can carry a
/// styled paragraph without changing language.
/// </para>
/// <para>
/// Hand-written rather than a Markdown package. The dialect is small enough to
/// read in one sitting, the output has to line up exactly with what
/// <see cref="EmailHtml"/> keeps, and a full CommonMark implementation would
/// mostly produce constructs this then strips — raw HTML blocks, code fences,
/// tables — which is a dependency whose extra features are all removed
/// downstream.
/// </para>
/// <para>
/// <c>{{placeholders}}</c> are ordinary text to every rule here and come out
/// the other side unchanged, in both parts. That is what lets
/// <see cref="TemplateRenderer.PlaceholdersIn"/> still find them after a
/// template has been saved.
/// </para>
/// </remarks>
public static partial class TemplateMarkdown
{
    /// <summary>How deep a blockquote may nest before it stops recursing.</summary>
    /// <remarks>
    /// A bound rather than a feature. Nothing in an announcement quotes four
    /// levels deep, and an unbounded recursion over text somebody pasted is a
    /// stack overflow in a web request.
    /// </remarks>
    private const int MaxDepth = 4;

    /// <summary>Markers around stashed fragments, stripped from input first.</summary>
    /// <remarks>
    /// Private-use characters, so a fragment set aside while emphasis is
    /// matched cannot be confused with anything an author typed.
    /// </remarks>
    private const char Open = '\uE000';
    private const char Shut = '\uE001';

    [GeneratedRegex(@"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex Heading { get; }

    [GeneratedRegex(@"^\s{0,3}([-*_])[ \t]*(\1[ \t]*){2,}$")]
    private static partial Regex Rule { get; }

    [GeneratedRegex(@"^\s{0,3}[-*+]\s+(.*)$")]
    private static partial Regex Bullet { get; }

    [GeneratedRegex(@"^\s{0,3}\d+[.)]\s+(.*)$")]
    private static partial Regex Numbered { get; }

    [GeneratedRegex(@"</?[A-Za-z][^>]*>")]
    private static partial Regex RawTag { get; }

    [GeneratedRegex(@"!\[([^\]]*)\]\(\s*([^)\s]*)\s*\)")]
    private static partial Regex Image { get; }

    [GeneratedRegex(@"\[([^\]]*)\]\(\s*([^)\s]*)\s*\)")]
    private static partial Regex Link { get; }

    [GeneratedRegex(@"\\([\\*_\[\]()#!>+.-])")]
    private static partial Regex Escaped { get; }

    [GeneratedRegex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*")]
    private static partial Regex StrongStars { get; }

    [GeneratedRegex(@"__(?=\S)(.+?)(?<=\S)__")]
    private static partial Regex StrongBars { get; }

    [GeneratedRegex(@"(?<![*\w])\*([^*\s](?:[^*]*[^*\s])?)\*(?![*\w])")]
    private static partial Regex EmStars { get; }

    [GeneratedRegex(@"(?<![_\w])_([^_\s](?:[^_]*[^_\s])?)_(?![_\w])")]
    private static partial Regex EmBars { get; }

    /// <summary>
    /// The HTML body for this source, safe to store and send.
    /// </summary>
    /// <remarks>
    /// One method rather than a render step and a sanitise step a caller has to
    /// remember to pair, because a caller that forgets the second one writes an
    /// unsanitised body into a column that is mailed to several hundred people.
    /// <see cref="EmailHtml.Sanitize"/> stays public for the bodies this did not
    /// produce — the seeded <c>magic_link</c> row was written as HTML by hand.
    /// </remarks>
    public static string ToSafeHtml(string? markdown) =>
        EmailHtml.Sanitize(ToHtml(markdown));

    /// <summary>
    /// The plain-text body for this source.
    /// </summary>
    /// <remarks>
    /// Derived from the Markdown rather than from the generated HTML. Markdown
    /// already is the plain-text version of itself for the most part, so this
    /// removes markers rather than reconstructing prose out of tags — and a URL
    /// survives as a URL somebody can copy, which is the whole reason a text
    /// part is worth having.
    /// </remarks>
    public static string ToText(string? markdown)
    {
        var text = new StringBuilder();

        // Scripts and stylesheets go first, contents and all. Everything else
        // raw loses its tags further down and keeps its words, which is right
        // for a pasted <div> and wrong for the body of a <script>: that is
        // code, and printing it into a plain-text email is printing it to the
        // reader.
        Text(Lines(EmailHtml.WithoutDiscarded(markdown)), text, depth: 0);
        return text.ToString().TrimEnd();
    }

    /// <summary>The generated HTML before the allow-list has seen it.</summary>
    /// <remarks>
    /// Private on purpose. Raw HTML an author pasted is passed straight through
    /// here — deciding what may stay is <see cref="EmailHtml"/>'s job and only
    /// its job, so that there is one file to read when the question is what a
    /// template is allowed to contain.
    /// </remarks>
    private static string ToHtml(string? markdown)
    {
        var html = new StringBuilder();
        Html(Lines(markdown), html, depth: 0);
        return html.ToString();
    }

    private static List<string> Lines(string? markdown) =>
        (markdown ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

    // ---------------------------------------------------------------- html ---

    private static void Html(List<string> lines, StringBuilder html, int depth)
    {
        var i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];

            if (line.Trim().Length == 0)
            {
                i++;
                continue;
            }

            if (Rule.IsMatch(line))
            {
                html.Append("<hr />");
                i++;
                continue;
            }

            if (Heading.Match(line) is { Success: true } heading)
            {
                var level = heading.Groups[1].Value.Length.ToString(CultureInfo.InvariantCulture);
                html.Append("<h").Append(level).Append('>')
                    .Append(Inline(heading.Groups[2].Value))
                    .Append("</h").Append(level).Append('>');
                i++;
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                var quoted = Quoted(lines, ref i);
                html.Append("<blockquote>");

                if (depth < MaxDepth)
                {
                    Html(quoted, html, depth + 1);
                }
                else
                {
                    html.Append("<p>").Append(Inline(string.Join(" ", quoted))).Append("</p>");
                }

                html.Append("</blockquote>");
                continue;
            }

            var ordered = Numbered.IsMatch(line);
            if (ordered || Bullet.IsMatch(line))
            {
                var tag = ordered ? "ol" : "ul";
                html.Append('<').Append(tag).Append('>');

                while (i < lines.Count)
                {
                    var item = ordered ? Numbered.Match(lines[i]) : Bullet.Match(lines[i]);
                    if (!item.Success)
                    {
                        break;
                    }

                    html.Append("<li>").Append(Inline(item.Groups[1].Value)).Append("</li>");
                    i++;
                }

                html.Append("</").Append(tag).Append('>');
                continue;
            }

            // A paragraph, ending at the first blank line or the first line
            // that starts something else. Newlines inside it become <br />
            // rather than spaces: somebody writing an email who presses return
            // means it, and a paragraph that silently reflows is the first
            // thing they would report as a bug.
            var body = new List<string>();
            while (i < lines.Count && lines[i].Trim().Length > 0 && !Starts(lines[i]))
            {
                body.Add(lines[i].Trim());
                i++;
            }

            html.Append("<p>")
                .Append(string.Join("<br />", body.Select(Inline)))
                .Append("</p>");
        }
    }

    private static bool Starts(string line) =>
        Rule.IsMatch(line)
        || Heading.IsMatch(line)
        || Bullet.IsMatch(line)
        || Numbered.IsMatch(line)
        || line.TrimStart().StartsWith('>');

    /// <summary>Consumes a run of quoted lines and hands back their contents.</summary>
    private static List<string> Quoted(List<string> lines, ref int i)
    {
        var inner = new List<string>();

        while (i < lines.Count
               && lines[i].Trim().Length > 0
               && lines[i].TrimStart().StartsWith('>'))
        {
            var stripped = lines[i].TrimStart()[1..];
            inner.Add(stripped.StartsWith(' ') ? stripped[1..] : stripped);
            i++;
        }

        return inner;
    }

    /// <summary>
    /// Links, images, emphasis and escapes within one line.
    /// </summary>
    /// <remarks>
    /// Everything that is not emphasis is set aside first and put back last.
    /// Without that, a URL is text like any other and
    /// <c>http://example.com/a_b_c</c> comes out with an <c>&lt;em&gt;</c> in
    /// the middle of it.
    /// </remarks>
    private static string Inline(string text)
    {
        var stashed = new List<string>();

        var work = text.Replace(Open.ToString(), string.Empty, StringComparison.Ordinal)
                       .Replace(Shut.ToString(), string.Empty, StringComparison.Ordinal);

        work = Escaped.Replace(work, m => Stash(stashed, Encode(m.Groups[1].Value)));
        work = RawTag.Replace(work, m => Stash(stashed, m.Value));
        work = Image.Replace(work, m => Stash(
            stashed,
            $"<img src=\"{Encode(m.Groups[2].Value)}\" alt=\"{Encode(m.Groups[1].Value)}\" />"));
        work = Link.Replace(work, m => Stash(
            stashed,
            $"<a href=\"{Encode(m.Groups[2].Value)}\">{Emphasis(m.Groups[1].Value)}</a>"));

        return Restore(Emphasis(work), stashed);
    }

    private static string Emphasis(string text)
    {
        text = StrongStars.Replace(text, "<strong>$1</strong>");
        text = StrongBars.Replace(text, "<strong>$1</strong>");
        text = EmStars.Replace(text, "<em>$1</em>");
        text = EmBars.Replace(text, "<em>$1</em>");
        return text;
    }

    /// <summary>
    /// Escapes a value that is about to be written into an attribute.
    /// </summary>
    /// <remarks>
    /// Only the characters that would end the attribute early. A URL is
    /// otherwise left as typed, including the <c>&amp;</c> in a query string,
    /// which <see cref="EmailHtml"/> normalises when it re-reads the tag.
    /// </remarks>
    private static string Encode(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("\"", "&quot;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string Stash(List<string> stashed, string fragment)
    {
        stashed.Add(fragment);
        return string.Create(
            CultureInfo.InvariantCulture, $"{Open}{stashed.Count - 1}{Shut}");
    }

    /// <summary>
    /// Puts the set-aside fragments back.
    /// </summary>
    /// <remarks>
    /// Repeatedly, because a fragment can contain another: the text of a link
    /// may hold a raw tag that was set aside before the link was.
    /// </remarks>
    private static string Restore(string text, List<string> stashed)
    {
        for (var pass = 0; pass < MaxDepth && text.IndexOf(Open) >= 0; pass++)
        {
            var rebuilt = new StringBuilder(text.Length);
            var i = 0;

            while (i < text.Length)
            {
                if (text[i] != Open)
                {
                    rebuilt.Append(text[i]);
                    i++;
                    continue;
                }

                var end = text.IndexOf(Shut, i + 1);
                if (end < 0
                    || !int.TryParse(
                        text[(i + 1)..end], CultureInfo.InvariantCulture, out var index)
                    || index < 0
                    || index >= stashed.Count)
                {
                    i++;
                    continue;
                }

                rebuilt.Append(stashed[index]);
                i = end + 1;
            }

            text = rebuilt.ToString();
        }

        return text;
    }

    // ---------------------------------------------------------------- text ---

    private static void Text(List<string> lines, StringBuilder text, int depth)
    {
        var i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];

            if (line.Trim().Length == 0)
            {
                i++;
                continue;
            }

            if (Rule.IsMatch(line))
            {
                text.Append("---\n\n");
                i++;
                continue;
            }

            if (Heading.Match(line) is { Success: true } heading)
            {
                text.Append(Plain(heading.Groups[2].Value)).Append("\n\n");
                i++;
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                var quoted = Quoted(lines, ref i);
                var inner = new StringBuilder();

                if (depth < MaxDepth)
                {
                    Text(quoted, inner, depth + 1);
                }
                else
                {
                    inner.Append(Plain(string.Join(" ", quoted)));
                }

                foreach (var quotedLine in inner.ToString().TrimEnd().Split('\n'))
                {
                    text.Append("> ").Append(quotedLine).Append('\n');
                }

                text.Append('\n');
                continue;
            }

            var ordered = Numbered.IsMatch(line);
            if (ordered || Bullet.IsMatch(line))
            {
                var number = 1;
                while (i < lines.Count)
                {
                    var item = ordered ? Numbered.Match(lines[i]) : Bullet.Match(lines[i]);
                    if (!item.Success)
                    {
                        break;
                    }

                    text.Append(ordered
                            ? string.Create(CultureInfo.InvariantCulture, $"{number++}. ")
                            : "- ")
                        .Append(Plain(item.Groups[1].Value))
                        .Append('\n');
                    i++;
                }

                text.Append('\n');
                continue;
            }

            while (i < lines.Count && lines[i].Trim().Length > 0 && !Starts(lines[i]))
            {
                text.Append(Plain(lines[i].Trim())).Append('\n');
                i++;
            }

            text.Append('\n');
        }
    }

    /// <summary>One line of Markdown with its markers taken off.</summary>
    /// <remarks>
    /// A link becomes its text followed by its URL in angle brackets, because
    /// the point of the text part is that somebody reading it can still get
    /// where the email was sending them. An image becomes its alt text, which
    /// is the only part of it that survives having no pictures.
    /// </remarks>
    private static string Plain(string text)
    {
        var work = Escaped.Replace(text, "$1");

        // Raw HTML an author pasted has no place in the text part. Removed
        // rather than escaped: "&lt;p&gt;" in a plain-text email is worse than
        // the tag it came from. Before the links below rather than after,
        // because the "text <url>" a link becomes is itself tag-shaped and
        // would be the next thing stripped.
        work = RawTag.Replace(work, string.Empty);

        work = Image.Replace(work, m => m.Groups[1].Value);
        work = Link.Replace(work, m =>
        {
            var label = m.Groups[1].Value.Trim();
            var url = m.Groups[2].Value.Trim();
            return label.Length == 0 || string.Equals(label, url, StringComparison.Ordinal)
                ? url
                : $"{label} <{url}>";
        });

        work = StrongStars.Replace(work, "$1");
        work = StrongBars.Replace(work, "$1");
        work = EmStars.Replace(work, "$1");
        work = EmBars.Replace(work, "$1");

        return WebUtility.HtmlDecode(work).Trim();
    }
}
