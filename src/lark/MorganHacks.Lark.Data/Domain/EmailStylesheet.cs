using System.Text;
using System.Text.RegularExpressions;

namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// Reduces a <c>&lt;style&gt;</c> block to the rules an email may carry.
/// </summary>
/// <remarks>
/// The block used to be thrown away whole, contents and all, on the reasoning
/// that the body of a <c>&lt;style&gt;</c> is code rather than prose and
/// unwrapping it would print the code into the email. Throwing it away is a
/// third thing, and it was wrong: a designed email is a stylesheet and a
/// skeleton, and deleting the stylesheet leaves the skeleton to be drawn with
/// whatever margins the reader's client happens to default to. What arrived
/// was not an unstyled version of the email — it was a different email, with
/// gaps nobody put there.
/// <para>
/// <b>The same allow-list, one level up.</b> A declaration is checked by
/// <see cref="EmailStyle"/>, which already decides what a property and a value
/// may be, and is not duplicated here. This file adds only what a stylesheet
/// has that an attribute does not: selectors, and at-rules.
/// </para>
/// <para>
/// <b>Why a selector is safe and still checked.</b> A selector cannot execute
/// anything — it names elements. The danger is narrower and worth stating,
/// because it is the only one: the contents of a <c>&lt;style&gt;</c> element
/// are raw text to a parser, ended by nothing except <c>&lt;/style</c>. A
/// <c>&lt;</c> anywhere in what is emitted could close the element early and
/// put whatever follows into the document as markup. So nothing containing
/// <c>&lt;</c> is ever written out, which makes the escape impossible rather
/// than merely unlikely.
/// </para>
/// <para>
/// <b>What is deliberately missing.</b> <c>@import</c> fetches a stylesheet
/// from somewhere else, which is a request a reader's client makes on opening
/// the mail and a tracker by another name. <c>@font-face</c> is the same
/// request wearing a different hat. Neither is refused by name — like
/// everything else here, they are simply not among the two at-rules that are
/// allowed through.
/// </para>
/// </remarks>
public static partial class EmailStylesheet
{
    /// <summary>
    /// At-rules that may be kept, and whose inner rules are sanitised.
    /// </summary>
    /// <remarks>
    /// <c>@media</c> because a responsive email is a media query and nothing
    /// else, and stripping it is how a template that reads perfectly on a
    /// laptop arrives four hundred pixels wide on a phone.
    /// <para>
    /// <c>@supports</c> for the same reason one step removed: it is how an
    /// author says "use this only where it works", and dropping it keeps the
    /// fallback everywhere rather than the good version somewhere.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> NestedAtRules =
        new(StringComparer.OrdinalIgnoreCase) { "media", "supports" };

    /// <summary>
    /// What a selector may be made of.
    /// </summary>
    /// <remarks>
    /// Wide enough for everything email uses — classes, ids, descendants,
    /// child and sibling combinators, attribute selectors, pseudo-classes —
    /// and closed at the top, so a character nobody thought about is absent
    /// rather than considered. <c>&lt;</c> is not in it, which is the rule
    /// that matters.
    /// </remarks>
    [GeneratedRegex(@"^[A-Za-z0-9_\-\.\#\*\s,>\+~:\(\)\[\]=""'\|\^\$%]+$")]
    private static partial Regex Selector { get; }

    /// <summary>
    /// What an at-rule's prelude may be made of.
    /// </summary>
    /// <remarks>
    /// <c>screen and (max-width: 600px)</c> and its relatives. Narrower than a
    /// selector because a media query is a smaller language, and there is no
    /// reason to accept characters it does not use.
    /// </remarks>
    [GeneratedRegex(@"^[A-Za-z0-9_\-\.\s,:\(\)/]*$")]
    private static partial Regex Prelude { get; }

    /// <summary>A CSS comment, which is where a hidden word hides.</summary>
    /// <remarks>
    /// Removed before anything is read, for the reason <see cref="EmailStyle"/>
    /// gives: <c>expr/**/ession(</c> is one word to a browser and two to
    /// anybody matching on text.
    /// </remarks>
    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment { get; }

    /// <summary>
    /// Enough rules for any real email, and a stop for anything else.
    /// </summary>
    /// <remarks>
    /// A builder's export runs to a few hundred rules. A file that runs to
    /// thousands is either machine-generated noise or somebody testing what
    /// happens, and neither needs to be rendered before it is refused.
    /// </remarks>
    private const int MaxRules = 500;

    /// <summary>How deep the nesting may go. One at-rule, then rules.</summary>
    private const int MaxDepth = 2;

    /// <summary>The rules worth keeping, or an empty string.</summary>
    public static string Sanitize(string? css)
    {
        if (string.IsNullOrWhiteSpace(css))
        {
            return string.Empty;
        }

        var source = Comment.Replace(css, " ");
        var output = new StringBuilder(source.Length);
        var rules = 0;

        Write(source, 0, source.Length, output, ref rules, depth: 1);

        return output.ToString().Trim();
    }

    /// <summary>
    /// Reads rules out of one span and writes the ones that survive.
    /// </summary>
    /// <remarks>
    /// Recursive for the one case that nests: the body of an <c>@media</c> is
    /// a stylesheet, so it is read by the same code rather than by a second
    /// parser that would agree with this one until it did not.
    /// </remarks>
    private static void Write(
        string css, int from, int to, StringBuilder output, ref int rules, int depth)
    {
        var i = from;

        while (i < to && rules < MaxRules)
        {
            while (i < to && char.IsWhiteSpace(css[i]))
            {
                i++;
            }

            if (i >= to)
            {
                return;
            }

            // An at-rule without a block — @charset, @namespace — ends at its
            // semicolon and is dropped there. Looking for a brace instead would
            // swallow the rule that follows it.
            var open = css.IndexOf('{', i);
            var semicolon = css.IndexOf(';', i);

            if (open < 0 || open >= to)
            {
                return;
            }

            if (css[i] == '@' && semicolon >= 0 && semicolon < open)
            {
                i = semicolon + 1;
                continue;
            }

            var close = Closing(css, open, to);
            if (close < 0)
            {
                return;
            }

            var prelude = css[i..open].Trim();

            if (prelude.StartsWith('@'))
            {
                Nested(css, prelude, open, close, output, ref rules, depth);
            }
            else
            {
                Rule(prelude, css[(open + 1)..close], output, ref rules);
            }

            i = close + 1;
        }
    }

    /// <summary>One <c>@media</c> or <c>@supports</c>, if anything inside it survives.</summary>
    /// <remarks>
    /// The block is sanitised before the wrapper is written, so a query whose
    /// every rule was dropped does not leave an empty <c>@media</c> behind. An
    /// empty one is harmless and is also a puzzle for whoever reads the source
    /// later looking for the rule that went missing.
    /// </remarks>
    private static void Nested(
        string css, string prelude, int open, int close,
        StringBuilder output, ref int rules, int depth)
    {
        var space = prelude.IndexOfAny([' ', '\t', '\r', '\n', '(']);
        var name = space < 0 ? prelude[1..] : prelude[1..space];
        var query = space < 0 ? string.Empty : prelude[space..].Trim();

        if (depth >= MaxDepth
            || !NestedAtRules.Contains(name)
            || !Prelude.IsMatch(query))
        {
            return;
        }

        var inner = new StringBuilder();
        Write(css, open + 1, close, inner, ref rules, depth + 1);

        if (inner.Length == 0)
        {
            return;
        }

        output.Append('@').Append(name.ToLowerInvariant());
        if (query.Length > 0)
        {
            output.Append(' ').Append(query);
        }

        output.Append('{').Append(inner).Append('}');
    }

    /// <summary>One selector and the declarations that survive under it.</summary>
    private static void Rule(
        string selector, string declarations, StringBuilder output, ref int rules)
    {
        if (selector.Length == 0 || !Selector.IsMatch(selector))
        {
            return;
        }

        // The same allow-list an inline style attribute goes through. A
        // property that cannot be trusted in an attribute cannot be trusted in
        // a rule either, and one list is the only way that stays true.
        var body = EmailStyle.Sanitize(declarations);
        if (body.Length == 0)
        {
            return;
        }

        // Unreachable today, and kept.
        //
        // EmailStyle already drops any declaration whose value contains a `<`,
        // so nothing currently gets this far. It is here because of what it
        // guards: inside a <style> element there is no escaping, the element
        // ends at the first `</style`, and everything after it is markup in
        // the document. That failure is not a rendering bug, it is markup
        // injection, and the only input needed to cause it is one value the
        // list above decides to start allowing.
        //
        // Written as a check on what is about to be emitted rather than as a
        // claim about what can produce it, so that widening the value list
        // stays a rendering decision instead of quietly becoming a security
        // one.
        if (body.Contains('<'))
        {
            return;
        }

        // The selector as one line. A rule written across three lines in the
        // source is the same rule, and the newlines in it are the author's
        // indentation rather than anything CSS reads.
        output.Append(Whitespace.Replace(selector, " "))
              .Append('{').Append(body).Append('}');
        rules++;
    }

    /// <summary>
    /// The brace that closes the one at <paramref name="open"/>.
    /// </summary>
    /// <remarks>
    /// Counted rather than found, because the body of an <c>@media</c> holds
    /// braces of its own and the first <c>}</c> after it belongs to a rule
    /// inside it.
    /// </remarks>
    private static int Closing(string css, int open, int to)
    {
        var depth = 0;

        for (var i = open; i < to; i++)
        {
            depth += css[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace { get; }
}
