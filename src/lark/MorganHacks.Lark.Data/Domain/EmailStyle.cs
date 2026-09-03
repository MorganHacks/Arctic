using System.Text;
using System.Text.RegularExpressions;

namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// Reduces a <c>style</c> attribute to the declarations an email may carry.
/// </summary>
/// <remarks>
/// Inline CSS is how mail is styled. It is the one styling mechanism that
/// works in essentially every client, it is what every builder emits, and
/// without it there is no way to give a table cell a background colour — which
/// is to say no way to draw a button. So the attribute is allowed and its
/// contents are read rather than trusted.
/// <para>
/// <b>Allow-list, both halves.</b> The property has to be one of the names
/// below and the value has to be made of things that cannot execute. A
/// deny-list of <c>expression(</c> and <c>javascript:</c> would be a list of
/// the two attacks somebody remembered, and CSS has a comment syntax and an
/// escape syntax specifically good at spelling a word the reader of a
/// deny-list did not expect: <c>expr/**/ession(</c> and <c>\65 xpression(</c>
/// are the same word to a browser. Naming what may appear means neither of
/// those is a special case — they are simply not <c>color</c>.
/// </para>
/// <para>
/// <b>What is deliberately missing.</b> <c>position</c>, <c>behavior</c>,
/// <c>-moz-binding</c>, <c>filter</c> and every other property that has ever
/// been able to load or run something. Not listed as forbidden; just not
/// listed. <c>position</c> is absent for a second reason as well — no mail
/// client honours it, so a template that relies on it looks right only in the
/// preview pane.
/// </para>
/// </remarks>
public static partial class EmailStyle
{
    /// <summary>
    /// Every property a declaration may set.
    /// </summary>
    /// <remarks>
    /// Chosen from what a button, a bordered table and a piece of styled prose
    /// need, which is most of what event mail is. Longhand and shorthand both,
    /// because a builder emits one and a person writes the other.
    /// </remarks>
    private static readonly HashSet<string> Properties =
        new(StringComparer.Ordinal)
        {
            // Colour and background. The url() in a background is checked
            // against the same schemes an <img> is, below.
            "color", "background", "background-color", "background-image",
            "background-position", "background-repeat", "background-size",
            "opacity",

            // Type.
            "font", "font-family", "font-size", "font-style", "font-weight",
            "font-variant", "line-height", "letter-spacing", "word-spacing",
            "text-align", "text-align-last", "text-decoration",
            "text-decoration-color", "text-decoration-line", "text-indent",
            "text-transform", "vertical-align", "white-space", "word-break",
            "word-wrap", "overflow-wrap", "direction", "unicode-bidi",

            // Box.
            "margin", "margin-top", "margin-right", "margin-bottom", "margin-left",
            "padding", "padding-top", "padding-right", "padding-bottom",
            "padding-left",
            "width", "min-width", "max-width",
            "height", "min-height", "max-height",
            "display", "float", "clear", "overflow", "visibility",

            // Borders, which in email are how a button gets its edges.
            "border", "border-top", "border-right", "border-bottom", "border-left",
            "border-color", "border-style", "border-width",
            "border-top-color", "border-top-style", "border-top-width",
            "border-right-color", "border-right-style", "border-right-width",
            "border-bottom-color", "border-bottom-style", "border-bottom-width",
            "border-left-color", "border-left-style", "border-left-width",
            "border-radius", "border-top-left-radius", "border-top-right-radius",
            "border-bottom-left-radius", "border-bottom-right-radius",
            "border-collapse", "border-spacing",

            // Tables.
            "table-layout", "caption-side", "empty-cells",

            // Lists.
            "list-style", "list-style-type", "list-style-position",

            // Word's own, which Outlook reads and everything else ignores.
            // Inert anywhere it is not understood, and leaving them out means
            // a builder's export loses its Outlook spacing for no gain.
            "mso-line-height-rule", "mso-padding-alt", "mso-table-lspace",
            "mso-table-rspace", "mso-hide",
        };

    /// <summary>
    /// Functions a value may call.
    /// </summary>
    /// <remarks>
    /// Colours, and <c>url()</c> — which is allowed only in a background, and
    /// only pointing somewhere an image may point. Anything else that looks
    /// like a call, including an unnamed bracket, takes its declaration with
    /// it: <c>expression()</c> and <c>-moz-binding: url(...)</c> are both
    /// simply not on this list.
    /// </remarks>
    private static readonly HashSet<string> Functions =
        new(StringComparer.OrdinalIgnoreCase) { "rgb", "rgba", "hsl", "hsla", "url" };

    /// <summary>Properties whose value may name an image.</summary>
    private static readonly HashSet<string> Backgrounds =
        new(StringComparer.Ordinal) { "background", "background-image" };

    /// <summary>A CSS comment, which is where a hidden word hides.</summary>
    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment { get; }

    /// <summary>A property name, before it is looked up.</summary>
    [GeneratedRegex("^-?[a-z][a-z0-9-]*$")]
    private static partial Regex PropertyName { get; }

    /// <summary>An opening bracket and whatever name is in front of it.</summary>
    [GeneratedRegex(@"([A-Za-z_][A-Za-z0-9_-]*)?\s*\(")]
    private static partial Regex Call { get; }

    /// <summary>The inside of a <c>url()</c>, quoted or not.</summary>
    [GeneratedRegex(@"url\(\s*(?:""([^""]*)""|'([^']*)'|([^)]*))\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex Url { get; }

    /// <summary>The only bang a value may end with.</summary>
    [GeneratedRegex(@"!\s*important\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Important { get; }

    /// <summary>
    /// The declarations worth keeping, or an empty string if none are.
    /// </summary>
    /// <remarks>
    /// The caller hands in the decoded attribute value and writes the answer
    /// back out encoded, so <c>&amp;#101;xpression(</c> is read here as the
    /// word it becomes rather than as the letters it was typed as.
    /// <para>
    /// A declaration this cannot vouch for is dropped and the rest of the
    /// attribute is kept. Dropping the whole attribute over one property would
    /// be dropping the colour of a button because somebody also asked for a
    /// CSS animation.
    /// </para>
    /// </remarks>
    public static string Sanitize(string? css)
    {
        if (string.IsNullOrWhiteSpace(css))
        {
            return string.Empty;
        }

        var kept = new StringBuilder();

        foreach (var declaration in Declarations(Comment.Replace(css, " ")))
        {
            var colon = declaration.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var name = declaration[..colon].Trim().ToLowerInvariant();
            var value = declaration[(colon + 1)..].Trim();

            if (!PropertyName.IsMatch(name) || !Properties.Contains(name))
            {
                continue;
            }

            if (!IsSafeValue(name, value))
            {
                continue;
            }

            if (kept.Length > 0)
            {
                kept.Append(' ');
            }

            kept.Append(name).Append(": ").Append(value).Append(';');
        }

        return kept.ToString();
    }

    /// <summary>
    /// The declarations in an attribute, split on the semicolons that separate
    /// them rather than on every semicolon.
    /// </summary>
    /// <remarks>
    /// <c>url(data:text/html;base64,...)</c> is one declaration with a
    /// semicolon inside it. Splitting on every semicolon cuts it into a first
    /// half whose <c>url(</c> has no closing bracket left to check and a
    /// second half that is discarded — which is a <c>data:</c> URL surviving
    /// because it was written with a semicolon in it. So the split stops at
    /// brackets and quotes, and <see cref="IsSafeValue"/> refuses anything
    /// still unbalanced when it gets there.
    /// </remarks>
    private static IEnumerable<string> Declarations(string css)
    {
        var start = 0;
        var depth = 0;
        var quote = '\0';

        for (var i = 0; i < css.Length; i++)
        {
            var c = css[i];

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '"':
                case '\'':
                    quote = c;
                    break;

                case '(':
                    depth++;
                    break;

                case ')':
                    depth = Math.Max(0, depth - 1);
                    break;

                case ';' when depth == 0:
                    yield return css[start..i];
                    start = i + 1;
                    break;

                default:
                    break;
            }
        }

        yield return css[start..];
    }

    /// <summary>Whether a value is made only of things that cannot execute.</summary>
    /// <remarks>
    /// The backslash rule is the one worth explaining. CSS lets any character
    /// be written as an escape, so a value is free to spell a scheme or a
    /// function name in a way no substring check finds. Nothing an email needs
    /// requires one — not a colour, not a length, not a font stack — so a value
    /// containing one is refused rather than unescaped and re-examined.
    /// </remarks>
    private static bool IsSafeValue(string property, string value)
    {
        if (value.Length == 0 || value.Length > 512)
        {
            return false;
        }

        foreach (var c in value)
        {
            // < and > would end the attribute's tag when re-encoded wrongly by
            // something downstream; @ is the start of an at-rule; braces are
            // the start of a second declaration block; a backslash is an
            // escape. None of them belong in a declaration this allows.
            if (c is '\\' or '<' or '>' or '{' or '}' or '@' || char.IsControl(c))
            {
                return false;
            }
        }

        // "!important" is the only bang CSS has, and it is common enough in
        // email that refusing it would refuse half of what a builder emits.
        if (value.Contains('!', StringComparison.Ordinal) && !Important.IsMatch(value))
        {
            return false;
        }

        // An unbalanced bracket means a function call whose contents this
        // cannot see the end of, and therefore cannot check. Refused rather
        // than repaired: guessing where a value ends is guessing.
        var depth = 0;
        foreach (var c in value)
        {
            depth += c switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth < 0)
            {
                return false;
            }
        }

        if (depth != 0)
        {
            return false;
        }

        foreach (Match call in Call.Matches(value))
        {
            var function = call.Groups[1].Value;
            if (function.Length == 0 || !Functions.Contains(function))
            {
                return false;
            }

            if (string.Equals(function, "url", StringComparison.OrdinalIgnoreCase)
                && !Backgrounds.Contains(property))
            {
                return false;
            }
        }

        foreach (Match url in Url.Matches(value))
        {
            var target = url.Groups[1].Success ? url.Groups[1].Value
                : url.Groups[2].Success ? url.Groups[2].Value
                : url.Groups[3].Value;

            if (!EmailHtml.IsImageUrl(target))
            {
                return false;
            }
        }

        return true;
    }
}
