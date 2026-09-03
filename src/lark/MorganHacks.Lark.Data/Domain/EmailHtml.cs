using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// Reduces arbitrary HTML to the small set an email may contain.
/// </summary>
/// <remarks>
/// The authors are organizers rather than strangers, so this is not a defence
/// against an attacker typing into the box. It is a defence against the shape
/// of the thing being written: a template is stored once and mailed to several
/// hundred people who cannot be un-mailed, from a domain whose reputation is
/// the only reason sign-in links arrive. A pasted newsletter carrying a
/// tracking script, or an <c>onerror</c> left on an image by whatever editor it
/// came out of, is a thing somebody does by accident on a Tuesday.
/// <para>
/// <b>Allow-list, not deny-list.</b> Everything not named below is removed,
/// which is why an attribute called <c>onclick</c> needs no special case — it
/// is not <c>href</c>, <c>src</c>, <c>alt</c> or <c>title</c>, so it does not
/// survive. A deny-list would need updating every time browsers grew a new
/// event attribute, and would be wrong in between.
/// </para>
/// <para>
/// <b>Styling is inline, and inline styling is allowed.</b> A <c>style</c>
/// attribute is how every piece of marketing mail ever sent has been built,
/// and it works in essentially every client; its contents are read by
/// <see cref="EmailStyle"/> rather than trusted. <c>class</c> is allowed too —
/// it does nothing on its own, and refusing it only mangles what somebody
/// pasted out of a builder.
/// </para>
/// <para>
/// <b>A <c>&lt;style&gt;</c> block is not.</b> That is the one styling
/// judgement that goes the other way, and it is not really about safety.
/// Gmail drops the block when a message is forwarded, so a template that
/// depends on one looks correct until the first person forwards it to a
/// friend; Outlook.com rewrites the selectors; and vouching for a whole
/// stylesheet means parsing selectors, <c>@import</c> and <c>@media</c>
/// rather than the flat list of declarations an attribute holds. Every layout
/// that needs it can be written inline, so the block is discarded with its
/// contents.
/// </para>
/// <para>
/// <b>Tables stay.</b> Outlook's flexbox and grid support is bad enough that a
/// table is still the layout mechanism email is built out of — a button is a
/// single-cell table with a background colour — so the table tags and the old
/// presentational attributes that make them behave are on the list. Email HTML
/// is old-fashioned by necessity and this reflects that rather than arguing
/// with it.
/// </para>
/// <para>
/// <b>Never JavaScript.</b> The genuine exception, and the one place the
/// product argument and the safety argument agree: every client strips it, so
/// it would only ever work in a preview pane, and attempting it is a thing
/// spam filters score against. <c>&lt;script&gt;</c> goes with its contents,
/// <c>on*</c> handlers are simply not on any list here, and no <c>href</c> or
/// <c>src</c> may name a <c>javascript:</c> or <c>data:</c> URL.
/// </para>
/// </remarks>
public static partial class EmailHtml
{
    /// <summary>
    /// Every tag an email may contain, and the attributes each may keep on top
    /// of <see cref="Global"/>.
    /// </summary>
    /// <remarks>
    /// Chosen for what survives a mail client rather than for what is valid
    /// HTML, which is why the presentational attributes HTML deprecated twenty
    /// years ago are here: <c>cellpadding</c> on a table is honoured by Outlook
    /// and <c>padding</c> in a stylesheet is not, so the deprecated one is the
    /// one that works.
    /// <para>
    /// <c>div</c>, <c>span</c>, <c>center</c> and <c>font</c> are kept for the
    /// same reason. They are what a builder emits and what a wrapper pasted out
    /// of an existing newsletter is made of; unwrapping them would leave the
    /// words and lose every colour and width hung on them.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string[]> Allowed =
        new(StringComparer.Ordinal)
        {
            ["p"] = ["align"],
            ["br"] = [],
            ["hr"] = ["align", "width", "size"],
            ["h1"] = ["align"],
            ["h2"] = ["align"],
            ["h3"] = ["align"],
            ["h4"] = ["align"],
            ["h5"] = ["align"],
            ["h6"] = ["align"],
            ["strong"] = [],
            ["b"] = [],
            ["em"] = [],
            ["i"] = [],
            ["u"] = [],
            ["small"] = [],
            ["ul"] = [],
            ["ol"] = [],
            ["li"] = [],
            ["blockquote"] = [],
            ["a"] = ["href", "title", "target"],
            ["img"] = ["src", "alt", "title", "width", "height", "border", "align",
                       "hspace", "vspace"],

            // The layout language. `role` is here for `role="presentation"`,
            // which is what stops a screen reader announcing a button as a
            // one-by-one data table.
            ["table"] = ["width", "height", "align", "border", "cellpadding",
                         "cellspacing", "bgcolor", "role"],
            ["thead"] = ["align", "valign", "bgcolor"],
            ["tbody"] = ["align", "valign", "bgcolor"],
            ["tfoot"] = ["align", "valign", "bgcolor"],
            ["tr"] = ["align", "valign", "bgcolor", "height"],
            ["td"] = ["width", "height", "align", "valign", "bgcolor", "colspan",
                      "rowspan", "nowrap"],
            ["th"] = ["width", "height", "align", "valign", "bgcolor", "colspan",
                      "rowspan", "nowrap"],
            ["caption"] = ["align"],

            // The wrappers.
            ["div"] = ["align"],
            ["span"] = [],
            ["center"] = [],
            ["font"] = ["color", "face", "size"],
        };

    /// <summary>
    /// Attributes every allowed tag may carry.
    /// </summary>
    /// <remarks>
    /// <c>style</c> is not passed through: <see cref="EmailStyle.Sanitize"/>
    /// reads it declaration by declaration and what comes back is what is
    /// written. <c>class</c> is passed through, because a class name is a
    /// string with nothing to match it in an email — harmless to keep and
    /// occasionally the only thing that makes a pasted builder export make
    /// sense to the person who pasted it.
    /// </remarks>
    private static readonly string[] Global = ["style", "class"];

    /// <summary>Tags that close themselves and hold nothing.</summary>
    private static readonly HashSet<string> Void =
        new(StringComparer.Ordinal) { "br", "hr", "img" };

    /// <summary>
    /// Tags whose contents go with them.
    /// </summary>
    /// <remarks>
    /// Everything else that is not allowed is unwrapped — an unknown wrapper
    /// disappears and the paragraph inside it stays, which is what somebody
    /// pasting from a page meant. These are the ones where the contents are not
    /// prose: the body of a <c>&lt;script&gt;</c> or a <c>&lt;style&gt;</c> is
    /// code, and unwrapping it would print the code into the email.
    /// <para>
    /// <c>svg</c>, <c>iframe</c>, <c>object</c> and <c>embed</c> are here
    /// rather than merely absent for the same reason: each can carry script or
    /// a remote document in its body, so the body has to go with the tag.
    /// </para>
    /// </remarks>
    internal static readonly HashSet<string> Discarded =
        new(StringComparer.Ordinal)
        {
            "script", "style", "iframe", "frame", "frameset", "object", "embed",
            "applet", "noscript", "svg", "math", "template", "textarea", "title",
            "head", "xmp",
        };

    /// <summary>
    /// Disallowed tags that never have a closing tag to search for.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Discarded"/> because "skip to the closing
    /// tag" on a <c>&lt;meta&gt;</c> would find none and swallow the rest of
    /// the email.
    /// </remarks>
    private static readonly HashSet<string> Empty =
        new(StringComparer.Ordinal)
        {
            "link", "meta", "base", "input", "source", "param", "col", "wbr", "area",
        };

    /// <summary>What the start of a URL has to look like to have a scheme.</summary>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:")]
    private static partial Regex Scheme { get; }

    /// <summary>Schemes a link may use.</summary>
    /// <remarks>
    /// <c>mailto</c> because "email us" is the most common link in this kind of
    /// mail. No <c>data:</c>: <c>data:text/html</c> is a page in a link, and
    /// the image case it would otherwise serve is blocked by every mail client
    /// anyway.
    /// </remarks>
    private static readonly HashSet<string> LinkSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    /// <summary>Schemes an image may use.</summary>
    private static readonly HashSet<string> ImageSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https" };

    /// <summary>
    /// The same HTML with everything that is not on the allow-list removed.
    /// </summary>
    /// <remarks>
    /// Text between tags is decoded and re-encoded rather than passed through,
    /// so a stray <c>&amp;</c> becomes <c>&amp;amp;</c> and an entity that was
    /// already correct stays correct. That is also what stops
    /// <c>&amp;#106;avascript:</c> from reaching a browser as a scheme this
    /// never saw.
    /// <para>
    /// <c>{{placeholders}}</c> pass through untouched, because they are neither
    /// tags nor entities. They are filled in later by
    /// <see cref="TemplateRenderer"/>, which escapes the value it substitutes.
    /// </para>
    /// </remarks>
    public static string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var output = new StringBuilder(html.Length);
        var text = new StringBuilder();

        // What is currently open, and whether each one was written out. A tag
        // that was unwrapped still gets a frame, so its closing tag is dropped
        // with it instead of closing something else.
        var open = new List<(string Name, bool Emitted)>();

        var i = 0;
        while (i < html.Length)
        {
            if (html[i] != '<')
            {
                text.Append(html[i]);
                i++;
                continue;
            }

            if (html.AsSpan(i).StartsWith("<!--", StringComparison.Ordinal))
            {
                Flush(output, text);
                var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3;
                continue;
            }

            if (i + 1 < html.Length && (html[i + 1] == '!' || html[i + 1] == '?'))
            {
                Flush(output, text);
                var end = html.IndexOf('>', i);
                i = end < 0 ? html.Length : end + 1;
                continue;
            }

            if (!TryReadTag(html, i, out var tag, out var next))
            {
                // "a < b" is arithmetic, not markup. Left as text, which the
                // flush below will encode.
                text.Append('<');
                i++;
                continue;
            }

            Flush(output, text);
            i = next;

            if (tag.Closing)
            {
                Close(output, open, tag.Name);
                continue;
            }

            if (Discarded.Contains(tag.Name) && !tag.SelfClosing)
            {
                i = SkipPast(html, tag.Name, i);
                continue;
            }

            if (Discarded.Contains(tag.Name) || Empty.Contains(tag.Name))
            {
                continue;
            }

            if (!Allowed.TryGetValue(tag.Name, out var attributes))
            {
                // Unwrapped: the tag goes, the words inside it stay.
                if (!tag.SelfClosing)
                {
                    open.Add((tag.Name, Emitted: false));
                }

                continue;
            }

            var written = Attributes(tag, attributes);
            if (written is null)
            {
                // An anchor with a javascript: href, or an image pointing at
                // one. The link is not worth keeping; the words in it are.
                if (!Void.Contains(tag.Name) && !tag.SelfClosing)
                {
                    open.Add((tag.Name, Emitted: false));
                }

                continue;
            }

            output.Append('<').Append(tag.Name).Append(written);

            if (Void.Contains(tag.Name))
            {
                output.Append(" />");
                continue;
            }

            output.Append('>');

            if (!tag.SelfClosing)
            {
                open.Add((tag.Name, Emitted: true));
            }
        }

        Flush(output, text);

        // Anything the author left open. Closed here rather than left dangling,
        // because an unclosed <a> in a mail client swallows the rest of the
        // message into the link.
        for (var k = open.Count - 1; k >= 0; k--)
        {
            if (open[k].Emitted)
            {
                output.Append("</").Append(open[k].Name).Append('>');
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// The same text with discarded elements and their contents removed.
    /// </summary>
    /// <remarks>
    /// For the plain-text part, which is derived from the Markdown source
    /// rather than from the sanitised HTML and would otherwise print the body
    /// of a <c>&lt;script&gt;</c> as if it were prose. Here rather than in
    /// <see cref="TemplateMarkdown"/> so that <see cref="Discarded"/> has one
    /// definition — two lists of what is dangerous agree until somebody adds to
    /// one of them.
    /// </remarks>
    public static string WithoutDiscarded(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var output = new StringBuilder(html.Length);
        var i = 0;

        while (i < html.Length)
        {
            if (html[i] == '<'
                && TryReadTag(html, i, out var tag, out var next)
                && !tag.Closing
                && !tag.SelfClosing
                && Discarded.Contains(tag.Name))
            {
                i = SkipPast(html, tag.Name, next);
                continue;
            }

            output.Append(html[i]);
            i++;
        }

        return output.ToString();
    }

    /// <summary>Whether a URL is one this may point at.</summary>
    /// <remarks>
    /// Decoded and stripped of control characters before the scheme is read,
    /// because <c>java&#9;script:</c> and <c>&amp;#106;avascript:</c> are both
    /// the scheme a browser eventually sees.
    /// <para>
    /// A URL with no scheme is allowed. That covers the relative case and, more
    /// importantly, <c>{{link}}</c> — the sign-in template's href is a
    /// placeholder and nothing else, and refusing it would refuse the one
    /// template this system cannot run without.
    /// </para>
    /// </remarks>
    private static bool IsSafe(string value, IReadOnlySet<string> schemes)
    {
        var decoded = WebUtility.HtmlDecode(value);
        var cleaned = new StringBuilder(decoded.Length);

        foreach (var c in decoded)
        {
            if (!char.IsControl(c) && c != ' ')
            {
                cleaned.Append(c);
            }
        }

        var url = cleaned.ToString().Trim();
        if (url.Length == 0)
        {
            return false;
        }

        var scheme = Scheme.Match(url);
        return !scheme.Success || schemes.Contains(scheme.Value[..^1]);
    }

    /// <summary>Whether an image may be loaded from this URL.</summary>
    /// <remarks>
    /// Public to the assembly so that <see cref="EmailStyle"/> asks the same
    /// question of a <c>background-image</c> that this asks of an
    /// <c>&lt;img src&gt;</c>. Two answers to "may an email load this" would
    /// agree until one of them was changed.
    /// </remarks>
    internal static bool IsImageUrl(string value) => IsSafe(value, ImageSchemes);

    /// <summary>
    /// The attributes to write, or null if the whole tag has to go.
    /// </summary>
    /// <remarks>
    /// A <c>style</c> that survives as nothing is left off rather than written
    /// empty, and it never takes its tag with it: an author who asked for one
    /// colour this cannot vouch for has still written a paragraph.
    /// </remarks>
    private static string? Attributes(Tag tag, string[] allowed)
    {
        var written = new StringBuilder();
        var url = tag.Name switch { "a" => "href", "img" => "src", _ => null };
        var found = false;

        foreach (var (name, value) in tag.Attributes)
        {
            if (!allowed.Contains(name, StringComparer.Ordinal)
                && !Global.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            if (name == "style")
            {
                var declarations = EmailStyle.Sanitize(WebUtility.HtmlDecode(value));
                if (declarations.Length > 0)
                {
                    written.Append(" style=\"")
                           .Append(WebUtility.HtmlEncode(declarations))
                           .Append('"');
                }

                continue;
            }

            if (name == url)
            {
                var schemes = tag.Name == "img" ? ImageSchemes : LinkSchemes;
                if (!IsSafe(value, schemes))
                {
                    return null;
                }

                found = true;
            }

            written.Append(' ').Append(name).Append("=\"")
                   .Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(value)))
                   .Append('"');
        }

        // An <a> with no href is not a link and an <img> with no src is not an
        // image. Both are dropped rather than written out inert.
        return url is not null && !found ? null : written.ToString();
    }

    /// <summary>Closes down to the nearest matching open tag, if there is one.</summary>
    private static void Close(
        StringBuilder output, List<(string Name, bool Emitted)> open, string name)
    {
        var at = open.FindLastIndex(frame => frame.Name == name);
        if (at < 0)
        {
            // A closing tag with nothing to close. Dropped: emitting it would
            // close somebody else's element.
            return;
        }

        for (var k = open.Count - 1; k >= at; k--)
        {
            if (open[k].Emitted)
            {
                output.Append("</").Append(open[k].Name).Append('>');
            }

            open.RemoveAt(k);
        }
    }

    /// <summary>Where the contents of a discarded element end.</summary>
    internal static int SkipPast(string html, string name, int from)
    {
        var close = html.IndexOf("</" + name, from, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            return html.Length;
        }

        var end = html.IndexOf('>', close);
        return end < 0 ? html.Length : end + 1;
    }

    private static void Flush(StringBuilder output, StringBuilder text)
    {
        if (text.Length == 0)
        {
            return;
        }

        output.Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(text.ToString())));
        text.Clear();
    }

    internal sealed record Tag(
        string Name,
        bool Closing,
        bool SelfClosing,
        IReadOnlyList<(string Name, string Value)> Attributes);

    /// <summary>
    /// Reads one tag, or decides that this <c>&lt;</c> does not start one.
    /// </summary>
    /// <remarks>
    /// Hand-rolled rather than regex-driven so that a quoted attribute value
    /// containing <c>&gt;</c> ends where it actually ends. A regex that stops
    /// at the first <c>&gt;</c> cuts such a tag in half, and the half it leaves
    /// behind is attacker-shaped in exactly the way this file exists to
    /// prevent.
    /// </remarks>
    internal static bool TryReadTag(string html, int start, out Tag tag, out int next)
    {
        tag = null!;
        next = start;

        var i = start + 1;
        var closing = false;

        if (i < html.Length && html[i] == '/')
        {
            closing = true;
            i++;
        }

        var nameStart = i;
        while (i < html.Length && char.IsAsciiLetterOrDigit(html[i]))
        {
            i++;
        }

        if (i == nameStart || !char.IsAsciiLetter(html[nameStart]))
        {
            return false;
        }

        var name = html[nameStart..i].ToLowerInvariant();
        var attributes = new List<(string, string)>();
        var selfClosing = false;
        var closed = false;

        while (i < html.Length)
        {
            while (i < html.Length && char.IsWhiteSpace(html[i]))
            {
                i++;
            }

            if (i >= html.Length)
            {
                return false;
            }

            if (html[i] == '>')
            {
                i++;
                closed = true;
                break;
            }

            if (html[i] == '/')
            {
                selfClosing = true;
                i++;
                continue;
            }

            var attrStart = i;
            while (i < html.Length && html[i] != '=' && html[i] != '>'
                   && html[i] != '/' && !char.IsWhiteSpace(html[i]))
            {
                i++;
            }

            if (i == attrStart)
            {
                // A character that can start neither an attribute nor an end.
                return false;
            }

            var attribute = html[attrStart..i].ToLowerInvariant();
            var value = string.Empty;

            while (i < html.Length && char.IsWhiteSpace(html[i]))
            {
                i++;
            }

            if (i < html.Length && html[i] == '=')
            {
                i++;
                while (i < html.Length && char.IsWhiteSpace(html[i]))
                {
                    i++;
                }

                if (i < html.Length && (html[i] == '"' || html[i] == '\''))
                {
                    var quote = html[i];
                    i++;
                    var valueStart = i;
                    while (i < html.Length && html[i] != quote)
                    {
                        i++;
                    }

                    if (i >= html.Length)
                    {
                        return false;
                    }

                    value = html[valueStart..i];
                    i++;
                }
                else
                {
                    var valueStart = i;
                    while (i < html.Length && html[i] != '>' && !char.IsWhiteSpace(html[i]))
                    {
                        i++;
                    }

                    value = html[valueStart..i];
                }
            }

            attributes.Add((attribute, value));
        }

        // A tag that runs off the end of the document was never a tag. Treated
        // as the text it is rather than closed on the author's behalf, because
        // guessing where an unterminated tag ends is guessing.
        if (!closed)
        {
            return false;
        }

        tag = new Tag(name, closing, selfClosing, attributes);
        next = i;
        return true;
    }
}
