using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Tests;

/// <summary>
/// The stylesheet, copied onto the elements it applies to.
/// </summary>
/// <remarks>
/// The block alone is not enough: Gmail drops it when a message is forwarded,
/// so a design that lives only in a block survives until somebody passes the
/// mail on. These check that what gets copied is what a browser would have
/// decided, because an inliner that gets the cascade wrong produces an email
/// that is confidently different from its own preview.
/// </remarks>
public class CssInlinerTests
{
    [Fact]
    public void A_type_rule_lands_on_the_element()
    {
        var html = CssInliner.Inline("<style>p{color: red}</style><p>Hi.</p>");

        Assert.Contains("<p style=\"color: red\">Hi.</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_block_stays_as_well()
    {
        // Inlining is the copy that survives forwarding; the block is the copy
        // that carries what cannot be inlined. Removing it would trade one
        // half-working email for another.
        var html = CssInliner.Inline("<style>p{color: red}</style><p>Hi.</p>");

        Assert.Contains("<style>p{color: red}</style>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_class_rule_lands_only_on_the_elements_carrying_it()
    {
        var html = CssInliner.Inline(
            "<style>.card{padding: 8px}</style>"
            + "<div class=\"card\">a</div><div>b</div>");

        Assert.Contains("<div class=\"card\" style=\"padding: 8px\">", html, StringComparison.Ordinal);
        Assert.Contains("<div>b</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_descendant_selector_needs_the_ancestor()
    {
        var html = CssInliner.Inline(
            "<style>.card p{margin: 0}</style>"
            + "<div class=\"card\"><p>in</p></div><p>out</p>");

        Assert.Contains("<p style=\"margin: 0\">in</p>", html, StringComparison.Ordinal);
        Assert.Contains("<p>out</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ancestor_does_not_have_to_be_the_parent()
    {
        var html = CssInliner.Inline(
            "<style>.card p{margin: 0}</style>"
            + "<div class=\"card\"><table><tr><td><p>deep</p></td></tr></table></div>");

        Assert.Contains("<p style=\"margin: 0\">deep</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_closed_ancestor_stops_matching()
    {
        // The stack has to pop. Without it every element after the first
        // </div> keeps inheriting a selector it is no longer inside.
        var html = CssInliner.Inline(
            "<style>.card p{margin: 0}</style>"
            + "<div class=\"card\"><p>in</p></div><div><p>after</p></div>");

        Assert.Contains("<p style=\"margin: 0\">in</p>", html, StringComparison.Ordinal);
        Assert.Contains("<p>after</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_more_specific_rule_wins()
    {
        // Written last-wins inside one attribute, so the weaker rule has to be
        // emitted first. An inliner that got this backwards would quietly
        // apply the base colour over the override.
        var html = CssInliner.Inline(
            "<style>.card{color: blue}p{color: red}</style>"
            + "<p class=\"card\">Hi.</p>");

        var style = Style(html);
        Assert.True(
            style.IndexOf("red", StringComparison.Ordinal)
            < style.IndexOf("blue", StringComparison.Ordinal),
            $"the class rule should win, and did not: {style}");
    }

    [Fact]
    public void An_id_beats_any_number_of_classes()
    {
        // Weighted rather than counted. Eleven classes must still lose to one
        // id, which is the case that catches an inliner adding the counts up.
        var html = CssInliner.Inline(
            "<style>#x{color: blue}.a.b.c{color: red}</style>"
            + "<p id=\"x\" class=\"a b c\">Hi.</p>");

        var style = Style(html);
        Assert.True(
            style.IndexOf("red", StringComparison.Ordinal)
            < style.IndexOf("blue", StringComparison.Ordinal),
            $"the id rule should win, and did not: {style}");
    }

    [Fact]
    public void Equal_specificity_is_broken_by_source_order()
    {
        var html = CssInliner.Inline(
            "<style>p{color: red}p{color: blue}</style><p>Hi.</p>");

        var style = Style(html);
        Assert.True(
            style.IndexOf("red", StringComparison.Ordinal)
            < style.IndexOf("blue", StringComparison.Ordinal),
            $"the later rule should win, and did not: {style}");
    }

    [Fact]
    public void What_the_author_wrote_on_the_element_wins()
    {
        // The one mechanism that must not run backwards. Somebody typing a
        // style attribute is overriding the stylesheet on purpose.
        var html = CssInliner.Inline(
            "<style>p{color: red}</style><p style=\"color: green\">Hi.</p>");

        var style = Style(html);
        Assert.True(
            style.IndexOf("red", StringComparison.Ordinal)
            < style.IndexOf("green", StringComparison.Ordinal),
            $"the author's own style should win, and did not: {style}");
    }

    [Fact]
    public void A_media_query_is_never_inlined()
    {
        // Conditional by definition. Inlining it applies it unconditionally,
        // which is an email that renders at its phone size on every screen.
        var html = CssInliner.Inline(
            "<style>@media screen and (max-width: 600px){p{font-size: 12px}}</style>"
            + "<p>Hi.</p>");

        Assert.Contains("<p>Hi.</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("style=\"font-size", html, StringComparison.Ordinal);
        Assert.Contains("@media", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".card > p")]
    [InlineData("p + p")]
    [InlineData("p:first-child")]
    [InlineData("a[href]")]
    public void A_selector_this_does_not_understand_is_left_to_the_block(string selector)
    {
        // Skipped rather than approximated. A selector inlined onto the wrong
        // element is worse than one left alone: the block still applies it
        // correctly wherever a block is read, and nowhere is made worse.
        var html = CssInliner.Inline(
            $"<style>{selector}{{color: red}}</style>"
            + "<div class=\"card\"><p>a</p><p><a href=\"#\">b</a></p></div>");

        Assert.DoesNotContain("style=", html, StringComparison.Ordinal);
        Assert.Contains("color: red", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_with_no_stylesheet_comes_back_untouched()
    {
        const string source = "<p>Hi.</p><div class=\"card\">a</div>";

        Assert.Equal(source, CssInliner.Inline(source));
    }

    [Fact]
    public void A_void_element_does_not_swallow_what_follows_it()
    {
        // <img> has no closing tag, so a stack that pushed it would leave it
        // open for the rest of the document and match descendants of nothing.
        var html = CssInliner.Inline(
            "<style>img p{color: red}</style><img src=\"https://a.invalid/x.png\"><p>Hi.</p>");

        Assert.Contains("<p>Hi.</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_written_onto_an_element_no_rule_matches()
    {
        var html = CssInliner.Inline("<style>.card{color: red}</style><p>Hi.</p>");

        Assert.Contains("<p>Hi.</p>", html, StringComparison.Ordinal);
    }

    /// <summary>The first style attribute in the document.</summary>
    private static string Style(string html)
    {
        var at = html.IndexOf("style=\"", html.IndexOf("</style>", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(at >= 0, $"no inlined style attribute in: {html}");

        var end = html.IndexOf('"', at + 7);
        return html[(at + 7)..end];
    }
}
