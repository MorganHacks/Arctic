using System.Text;
using System.Text.RegularExpressions;

namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// Copies a stylesheet's declarations onto the elements its selectors match.
/// </summary>
/// <remarks>
/// A <c>&lt;style&gt;</c> block is the natural way to write an email and the
/// least reliable way to send one. Gmail drops the block when a message is
/// forwarded, so a template that depends on one looks right until somebody
/// passes it on — which is exactly what happens to an announcement worth
/// reading. Some clients ignore it outright.
/// <para>
/// So the rules are copied onto the elements as well. The block stays where it
/// was: a media query cannot be inlined and neither can anything else that is
/// conditional, so it has to survive for the clients that do read it. What is
/// inlined is the unconditional part, which is the part that has to arrive
/// everywhere.
/// </para>
/// <para>
/// <b>Runs after sanitising, never before.</b> Everything here works on
/// <see cref="EmailHtml"/>'s output: tags are known, attributes are quoted,
/// and every declaration has already been through
/// <see cref="EmailStyle.Sanitize"/>. That is what makes a scanner sufficient
/// where a parser would otherwise be needed, and it is also what makes the
/// result safe — nothing is composed here that was not already allowed, only
/// moved.
/// </para>
/// <para>
/// <b>What inlining cannot reach, and it is not a bug here.</b> A rule
/// selecting <c>body</c> or <c>html</c> has nothing to land on: the sanitised
/// body is a fragment, and those elements belong to the document the mail
/// client builds around it. Such rules stay in the block and work wherever the
/// block is read, which is everywhere except a forwarded copy — so a template
/// whose typeface is set on <c>body</c> arrives in a forward with the client's
/// default face, while everything set on an element it actually contains
/// survives intact. Setting type on the outermost <c>div</c> rather than on
/// <c>body</c> is what an author can do about it, and is worth saying in the
/// editor rather than worked around here by inventing a wrapper element the
/// author did not write.
/// </para>
/// <para>
/// <b>What is deliberately not matched.</b> Child and sibling combinators,
/// pseudo-classes, attribute selectors. Each would be a matcher that is right
/// most of the time, and a selector inlined onto the wrong element is worse
/// than one left alone: the block still applies it correctly in every client
/// that reads a block, and the ones that do not are no worse off than they
/// were. An unsupported selector is skipped, not approximated.
/// </para>
/// </remarks>
public static partial class CssInliner
{
    /// <summary>Elements that hold nothing, so nothing can descend from them.</summary>
    private static readonly HashSet<string> Void =
        new(StringComparer.Ordinal) { "br", "hr", "img" };

    /// <summary>A tag, as it appears in already-sanitised output.</summary>
    [GeneratedRegex(@"<(/?)([a-z0-9]+)((?:\s+[a-z\-]+=""[^""]*"")*)\s*(/?)>")]
    private static partial Regex TagPattern { get; }

    /// <summary>One attribute inside a tag.</summary>
    [GeneratedRegex(@"([a-z\-]+)=""([^""]*)""")]
    private static partial Regex AttributePattern { get; }

    /// <summary>A compound selector: an optional type, then classes and ids.</summary>
    /// <remarks>
    /// Anchored at both ends, so anything carrying a combinator this does not
    /// understand — <c>&gt;</c>, <c>+</c>, <c>~</c>, a colon, a bracket —
    /// fails to parse and is left to the block rather than guessed at.
    /// </remarks>
    [GeneratedRegex(@"^(\*|[a-z][a-z0-9]*)?((?:[.#][A-Za-z0-9_\-]+)*)$")]
    private static partial Regex CompoundPattern { get; }

    /// <summary>
    /// The same HTML, with the stylesheet's unconditional rules also written
    /// onto the elements they match.
    /// </summary>
    public static string Inline(string? html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("<style>", StringComparison.Ordinal))
        {
            return html ?? string.Empty;
        }

        var rules = Rules(html);
        if (rules.Count == 0)
        {
            return html;
        }

        return Apply(html, rules);
    }

    /// <summary>
    /// Every rule in every style block, flattened, in source order.
    /// </summary>
    /// <remarks>
    /// At-rules are skipped whole. <c>@media</c> is conditional by definition,
    /// and inlining a conditional rule applies it unconditionally — a mobile
    /// override copied onto an element is an email that renders at its phone
    /// size on a desktop, everywhere, for everybody. The block keeps them.
    /// </remarks>
    private static List<Rule> Rules(string html)
    {
        var rules = new List<Rule>();

        foreach (Match block in Regex.Matches(
            html, @"<style>(.*?)</style>", RegexOptions.Singleline))
        {
            var css = block.Groups[1].Value;
            var i = 0;

            while (i < css.Length)
            {
                var open = css.IndexOf('{', i);
                if (open < 0)
                {
                    break;
                }

                var close = Closing(css, open);
                if (close < 0)
                {
                    break;
                }

                var prelude = css[i..open].Trim();

                // One of two things keeping at-rules out, and the redundancy is
                // deliberate. Skipping to `close` already steps over the whole
                // body, so the rules inside a query are never seen; this check
                // only stops the query's own prelude being read as a selector,
                // which would fail to parse anyway.
                //
                // Removing it changes nothing today — it was mutation-tested,
                // and no test noticed. It stays because the thing it guards is
                // not a rendering bug: a mobile override applied
                // unconditionally is every email arriving at its phone size,
                // on every screen, and the next person to make this recurse
                // into a body should have to delete something that says so.
                if (!prelude.StartsWith('@'))
                {
                    Add(rules, prelude, css[(open + 1)..close]);
                }

                i = close + 1;
            }
        }

        return rules;
    }

    /// <summary>One rule per selector in a comma-separated list.</summary>
    /// <remarks>
    /// Split first, because <c>h1, .card p</c> is two selectors with two
    /// different specificities, and treating the list as one would give the
    /// whole rule whichever specificity the reader of the list happened to
    /// compute.
    /// </remarks>
    private static void Add(List<Rule> rules, string selectors, string declarations)
    {
        var body = declarations.Trim();
        if (body.Length == 0)
        {
            return;
        }

        foreach (var selector in selectors.Split(','))
        {
            var chain = Chain(selector.Trim());
            if (chain is not null)
            {
                rules.Add(new Rule(chain, body, Specificity(chain), rules.Count));
            }
        }
    }

    /// <summary>
    /// A selector as compounds separated by descendant combinators, or null.
    /// </summary>
    private static Compound[]? Chain(string selector)
    {
        if (selector.Length == 0)
        {
            return null;
        }

        var parts = selector.Split(
            [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var chain = new Compound[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            var match = CompoundPattern.Match(parts[i]);
            if (!match.Success)
            {
                return null;
            }

            var type = match.Groups[1].Value;
            var rest = match.Groups[2].Value;

            if (type.Length == 0 && rest.Length == 0)
            {
                return null;
            }

            var classes = new List<string>();
            string? id = null;

            foreach (Match piece in Regex.Matches(rest, @"([.#])([A-Za-z0-9_\-]+)"))
            {
                if (piece.Groups[1].Value == ".")
                {
                    classes.Add(piece.Groups[2].Value);
                }
                else
                {
                    id = piece.Groups[2].Value;
                }
            }

            chain[i] = new Compound(
                type is "" or "*" ? null : type, classes.ToArray(), id);
        }

        return chain;
    }

    /// <summary>
    /// Ids, then classes, then types — the cascade's own ordering.
    /// </summary>
    /// <remarks>
    /// Weighted rather than counted into one number per category, because a
    /// rule with eleven classes must still lose to a rule with one id, and
    /// adding the counts together would have it win. The weights are wide
    /// enough that no real selector reaches them.
    /// </remarks>
    private static int Specificity(Compound[] chain)
    {
        var ids = 0;
        var classes = 0;
        var types = 0;

        foreach (var compound in chain)
        {
            ids += compound.Id is null ? 0 : 1;
            classes += compound.Classes.Length;
            types += compound.Tag is null ? 0 : 1;
        }

        return (ids * 10_000) + (classes * 100) + types;
    }

    /// <summary>Walks the document, writing matched declarations onto elements.</summary>
    private static string Apply(string html, List<Rule> rules)
    {
        var output = new StringBuilder(html.Length + (rules.Count * 32));
        var stack = new List<Element>();
        var at = 0;

        foreach (Match tag in TagPattern.Matches(html))
        {
            // A style block's contents are CSS, and a brace in it is not a
            // tag. Skipped whole rather than scanned, so a selector containing
            // something tag-shaped cannot be mistaken for markup.
            if (tag.Groups[2].Value == "style" && tag.Groups[1].Value.Length == 0)
            {
                var end = html.IndexOf("</style>", tag.Index, StringComparison.Ordinal);
                if (end >= 0)
                {
                    output.Append(html, at, end + 8 - at);
                    at = end + 8;
                    continue;
                }
            }

            if (tag.Index < at)
            {
                continue;
            }

            var name = tag.Groups[2].Value;

            if (tag.Groups[1].Value == "/")
            {
                for (var i = stack.Count - 1; i >= 0; i--)
                {
                    if (stack[i].Tag == name)
                    {
                        stack.RemoveRange(i, stack.Count - i);
                        break;
                    }
                }

                continue;
            }

            var attributes = Attributes(tag.Groups[3].Value);
            var element = Element.From(name, attributes);

            stack.Add(element);

            var declarations = Matched(rules, stack);

            if (declarations.Length > 0)
            {
                output.Append(html, at, tag.Index - at);
                output.Append(Rewritten(tag, attributes, declarations));
                at = tag.Index + tag.Length;
            }

            // Nothing descends from a void element, and the document has no
            // closing tag for one to pop it off again.
            if (Void.Contains(name) || tag.Groups[4].Value == "/")
            {
                stack.RemoveAt(stack.Count - 1);
            }
        }

        output.Append(html, at, html.Length - at);
        return output.ToString();
    }

    /// <summary>
    /// Every matching rule's declarations, weakest first.
    /// </summary>
    /// <remarks>
    /// Order is the whole of the cascade once everything is in one attribute:
    /// later declarations of the same property win, so sorting weakest-first
    /// and concatenating reproduces what a browser would have decided. Source
    /// order breaks the tie between equal specificities, which is also what a
    /// browser does.
    /// </remarks>
    private static string Matched(List<Rule> rules, List<Element> stack)
    {
        List<Rule>? hits = null;

        foreach (var rule in rules)
        {
            if (Matches(rule.Chain, stack))
            {
                (hits ??= []).Add(rule);
            }
        }

        if (hits is null)
        {
            return string.Empty;
        }

        hits.Sort((a, b) => a.Specificity == b.Specificity
            ? a.Order.CompareTo(b.Order)
            : a.Specificity.CompareTo(b.Specificity));

        var joined = new StringBuilder();

        foreach (var rule in hits)
        {
            if (joined.Length > 0)
            {
                joined.Append("; ");
            }

            joined.Append(rule.Declarations.TrimEnd(';', ' '));
        }

        return joined.ToString();
    }

    /// <summary>
    /// Whether a chain of descendant compounds matches the open element.
    /// </summary>
    /// <remarks>
    /// Right to left, greedily, without backtracking. That is exact for
    /// descendant combinators and only for those: every compound to the left
    /// needs some ancestor, any ancestor, and taking the nearest one can never
    /// make an earlier compound impossible to place. It would not be exact for
    /// a child combinator, which is one of the reasons those are not accepted.
    /// </remarks>
    private static bool Matches(Compound[] chain, List<Element> stack)
    {
        var c = chain.Length - 1;

        if (!Fits(chain[c], stack[^1]))
        {
            return false;
        }

        c--;

        for (var s = stack.Count - 2; s >= 0 && c >= 0; s--)
        {
            if (Fits(chain[c], stack[s]))
            {
                c--;
            }
        }

        return c < 0;
    }

    private static bool Fits(Compound compound, Element element)
    {
        if (compound.Tag is not null && compound.Tag != element.Tag)
        {
            return false;
        }

        if (compound.Id is not null && compound.Id != element.Id)
        {
            return false;
        }

        foreach (var name in compound.Classes)
        {
            if (!element.Classes.Contains(name))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The tag again, carrying the inlined declarations.
    /// </summary>
    /// <remarks>
    /// The element's own <c>style</c> goes last, so it wins. Somebody typing a
    /// style attribute on an element is overriding the stylesheet on purpose,
    /// and an inliner that let a rule beat it would be the one mechanism in
    /// the cascade that runs backwards.
    /// </remarks>
    private static string Rewritten(
        Match tag, List<(string Name, string Value)> attributes, string declarations)
    {
        var rebuilt = new StringBuilder("<").Append(tag.Groups[2].Value);
        var written = false;

        foreach (var (name, value) in attributes)
        {
            rebuilt.Append(' ').Append(name).Append("=\"");

            if (name == "style")
            {
                rebuilt.Append(declarations).Append("; ").Append(value);
                written = true;
            }
            else
            {
                rebuilt.Append(value);
            }

            rebuilt.Append('"');
        }

        if (!written)
        {
            rebuilt.Append(" style=\"").Append(declarations).Append('"');
        }

        return rebuilt.Append(tag.Groups[4].Value).Append('>').ToString();
    }

    private static List<(string Name, string Value)> Attributes(string source)
    {
        var attributes = new List<(string, string)>();

        foreach (Match attribute in AttributePattern.Matches(source))
        {
            attributes.Add((attribute.Groups[1].Value, attribute.Groups[2].Value));
        }

        return attributes;
    }

    private static int Closing(string css, int open)
    {
        var depth = 0;

        for (var i = open; i < css.Length; i++)
        {
            depth += css[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private sealed record Compound(string? Tag, string[] Classes, string? Id);

    private sealed record Rule(
        Compound[] Chain, string Declarations, int Specificity, int Order);

    private sealed record Element(string Tag, HashSet<string> Classes, string? Id)
    {
        public static Element From(
            string tag, List<(string Name, string Value)> attributes)
        {
            var classes = new HashSet<string>(StringComparer.Ordinal);
            string? id = null;

            foreach (var (name, value) in attributes)
            {
                if (name == "class")
                {
                    foreach (var one in value.Split(
                        ' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        classes.Add(one);
                    }
                }
                else if (name == "id")
                {
                    id = value;
                }
            }

            return new Element(tag, classes, id);
        }
    }
}
