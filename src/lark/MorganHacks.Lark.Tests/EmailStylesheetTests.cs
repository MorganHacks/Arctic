using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Tests;

/// <summary>
/// The stylesheet a designed email carries, and what survives of it.
/// </summary>
/// <remarks>
/// These exist because the block used to be deleted whole. What arrived was
/// not an unstyled version of the email — it was a different email, drawn with
/// whatever margins the reader's client defaulted to, and the author had no
/// way to tell from anywhere they could check it.
/// </remarks>
public class EmailStylesheetTests
{
    [Fact]
    public void A_rule_survives_with_its_selector_and_its_declarations()
    {
        var css = EmailStylesheet.Sanitize("p { margin: 0 0 12px; line-height: 1.5 }");

        Assert.Contains("p{", css, StringComparison.Ordinal);
        Assert.Contains("margin: 0 0 12px", css, StringComparison.Ordinal);
        Assert.Contains("line-height: 1.5", css, StringComparison.Ordinal);
    }

    [Fact]
    public void A_media_query_survives_with_the_rules_inside_it()
    {
        // A responsive email is a media query and nothing else. Stripping it is
        // how a template that reads perfectly on a laptop arrives four hundred
        // pixels wide on a phone.
        var css = EmailStylesheet.Sanitize(
            "@media screen and (max-width: 600px) { .wrap { padding: 8px } }");

        Assert.Contains("@media screen and (max-width: 600px){", css, StringComparison.Ordinal);
        Assert.Contains(".wrap{padding: 8px", css, StringComparison.Ordinal);
    }

    [Fact]
    public void An_import_is_dropped()
    {
        // A request the reader's client makes on opening the mail, to a host
        // the author chose. That is a tracker with a stylesheet's name on it,
        // and it is also a stylesheet nobody has sanitised.
        var css = EmailStylesheet.Sanitize(
            "@import url('https://example.invalid/a.css'); p { color: #333 }");

        Assert.DoesNotContain("import", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.invalid", css, StringComparison.Ordinal);

        // And the rule after it is still there: dropping an at-rule must not
        // swallow what follows.
        Assert.Contains("color: #333", css, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declaration_the_inline_allow_list_refuses_is_refused_here_too()
    {
        // One allow-list, at both levels. A property that cannot be trusted in
        // a style attribute cannot be trusted in a rule either, and two lists
        // agree until somebody adds to one of them.
        var css = EmailStylesheet.Sanitize(
            "p { position: absolute; behavior: url(x.htc); color: #333 }");

        Assert.DoesNotContain("position", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("behavior", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("color: #333", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_that_could_close_the_style_element_is_written()
    {
        // The one danger a stylesheet has that an attribute does not. Inside a
        // <style> element there is no escaping — the element ends at the first
        // </style, and everything after it is markup in the document.
        var css = EmailStylesheet.Sanitize(
            "p { font-family: '</style><script>alert(1)</script>' } div { color: red }");

        Assert.DoesNotContain("<", css, StringComparison.Ordinal);
        Assert.DoesNotContain("script", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_selector_carrying_a_bracket_takes_its_rule_with_it()
    {
        var css = EmailStylesheet.Sanitize("p<span { color: red } div { color: blue }");

        Assert.DoesNotContain("<", css, StringComparison.Ordinal);
        Assert.DoesNotContain("red", css, StringComparison.Ordinal);
        Assert.Contains("blue", css, StringComparison.Ordinal);
    }

    [Fact]
    public void A_comment_cannot_spell_a_word_past_the_allow_list()
    {
        // expr/**/ession( is one word to a browser and two to anybody matching
        // on text, which is why comments go before anything is read.
        var css = EmailStylesheet.Sanitize("p { colo/**/r: red }");

        Assert.DoesNotContain("red", css, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_media_query_leaves_nothing_behind()
    {
        // Harmless either way, and an empty @media is a puzzle for whoever
        // goes looking later for the rule that went missing.
        var css = EmailStylesheet.Sanitize("@media print { p { position: fixed } }");

        Assert.Equal(string.Empty, css);
    }

    [Fact]
    public void A_stylesheet_of_nothing_is_an_empty_string()
    {
        Assert.Equal(string.Empty, EmailStylesheet.Sanitize(null));
        Assert.Equal(string.Empty, EmailStylesheet.Sanitize("   "));
    }
}
