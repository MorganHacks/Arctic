using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Tests;

/// <summary>
/// What a template body is allowed to contain, tag by tag.
/// </summary>
/// <remarks>
/// The endpoint tests in atlas cover the same rules through the API, which is
/// where they matter. These are here because this is where the allow-list is,
/// and because a rule about a CSS escape sequence is easier to state and to
/// read as one line of HTML than as a saved template.
/// <para>
/// Every body below is deliberate nonsense. Template wording belongs to the
/// people who send the mail, and a plausible sentence in a test file is a
/// sentence somebody eventually copies into production.
/// </para>
/// </remarks>
public class EmailHtmlTests
{
    // ------------------------------------------------------------ what stays ---

    [Fact]
    public void An_inline_style_survives()
    {
        // The change this file exists for. Inline CSS is how every piece of
        // marketing mail is built, and it is the only styling that works in
        // essentially every client.
        var html = EmailHtml.Sanitize(
            "<p style=\"color: #123456; font-size: 16px\">placeholder</p>");

        Assert.Contains("color: #123456;", html, StringComparison.Ordinal);
        Assert.Contains("font-size: 16px;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_survives_with_the_attributes_that_make_it_behave()
    {
        // A button in email is a one-cell table with a background colour, and
        // Outlook honours cellpadding where it ignores a stylesheet.
        var html = EmailHtml.Sanitize(
            "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" "
            + "cellspacing=\"0\" border=\"0\" bgcolor=\"#ffffff\">"
            + "<tbody><tr><td align=\"center\" valign=\"top\" colspan=\"2\" "
            + "style=\"padding: 12px; background-color: #101010\">"
            + "<a href=\"https://example.invalid/go\">placeholder</a>"
            + "</td></tr></tbody></table>");

        foreach (var kept in new[]
        {
            "<table", "<tbody>", "<tr>", "<td", "role=\"presentation\"",
            "width=\"100%\"", "cellpadding=\"0\"", "cellspacing=\"0\"",
            "border=\"0\"", "bgcolor=\"#ffffff\"", "align=\"center\"",
            "valign=\"top\"", "colspan=\"2\"", "background-color: #101010;",
        })
        {
            Assert.Contains(kept, html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_class_is_left_alone()
    {
        // It matches nothing in an email and harms nothing. Stripping it only
        // mangles what somebody pasted out of a builder.
        var html = EmailHtml.Sanitize("<div class=\"wrapper outer\">placeholder</div>");

        Assert.Contains("class=\"wrapper outer\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_old_presentational_wrappers_are_kept()
    {
        var html = EmailHtml.Sanitize(
            "<center><font color=\"#ff0000\" face=\"Arial\" size=\"3\">"
            + "<span>placeholder</span></font></center>");

        Assert.Contains("<center>", html, StringComparison.Ordinal);
        Assert.Contains("<font color=\"#ff0000\"", html, StringComparison.Ordinal);
        Assert.Contains("<span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_background_image_may_be_loaded_over_https()
    {
        var html = EmailHtml.Sanitize(
            "<td style=\"background-image: url('https://example.invalid/a.png')\">x</td>");

        Assert.Contains("background-image:", html, StringComparison.Ordinal);
        Assert.Contains("example.invalid/a.png", html, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------- what goes ---

    [Fact]
    public void A_script_goes_with_its_contents()
    {
        var html = EmailHtml.Sanitize("<p>before</p><script>alert(1)</script><p>after</p>");

        Assert.DoesNotContain("alert", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before", html, StringComparison.Ordinal);
        Assert.Contains("after", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<iframe src=\"https://example.invalid/\">placeholder</iframe>")]
    [InlineData("<object data=\"https://example.invalid/\">placeholder</object>")]
    [InlineData("<embed src=\"https://example.invalid/\">")]
    [InlineData("<svg><desc>placeholder</desc></svg>")]
    public void The_document_embedders_go_with_their_contents(string markup)
    {
        var html = EmailHtml.Sanitize($"<p>kept</p>{markup}");

        Assert.Equal("<p>kept</p>", html);
    }

    [Fact]
    public void An_event_handler_is_not_an_attribute_this_knows()
    {
        var html = EmailHtml.Sanitize(
            "<td onclick=\"alert(1)\" onmouseover=\"alert(2)\" "
            + "style=\"color: red\">placeholder</td>");

        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onmouseover", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("color: red;", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("&#106;avascript:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("data:text/html,<h1>x</h1>")]
    public void A_link_may_not_name_a_scheme_that_runs_or_carries_a_document(string href)
    {
        var html = EmailHtml.Sanitize($"<a href=\"{href}\">placeholder</a>");

        Assert.DoesNotContain("<a", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", html, StringComparison.OrdinalIgnoreCase);

        // The words survive; only the link does not.
        Assert.Contains("placeholder", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_style_block_is_kept_and_read()
    {
        // This test used to assert the opposite, and the reason it gave was
        // true: Gmail drops the block when a message is forwarded, so a
        // template that depends on one looks right until somebody passes it
        // on.
        //
        // It was answering that by deleting the stylesheet for everybody, on
        // every send, in every client — trading a rare wrong render for a
        // certain one. What arrived was not the email unstyled; it was a
        // different email, drawn with whatever margins the reader's client
        // defaulted to, and nothing the author could open showed them that.
        //
        // The forwarding case is real and is not answered here. Answering it
        // means inlining these declarations onto the elements they match, so
        // that the block is a bonus rather than the only copy. Until that
        // exists, a stylesheet that survives is strictly closer to what the
        // author wrote than one that does not.
        var html = EmailHtml.Sanitize(
            "<style>p { color: red }</style><p>placeholder</p>");

        Assert.Contains("<style>p{color: red", html, StringComparison.Ordinal);
        Assert.Contains("<p>placeholder</p>", html, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------- style values ---

    [Theory]
    [InlineData("width: expression(alert(1))")]
    [InlineData("width: expr/**/ession(alert(1))")]
    [InlineData("background: url(javascript:alert(1))")]
    [InlineData("background-image: url(\"jav\\61 script:alert(1)\")")]
    [InlineData("color: red; behavior: url(#default#time2)")]
    [InlineData("-moz-binding: url(https://example.invalid/x.xml)")]
    [InlineData("background-image: url(data:text/html;base64,PHNjcmlwdD4=)")]
    public void A_declaration_that_could_execute_does_not_survive(string css)
    {
        var html = EmailHtml.Sanitize($"<p style=\"{css}\">placeholder</p>");

        foreach (var gone in new[] { "expression", "javascript", "behavior", "binding", "data:" })
        {
            Assert.DoesNotContain(gone, html, StringComparison.OrdinalIgnoreCase);
        }

        // The paragraph is not collateral. An author who asked for one thing
        // this cannot vouch for has still written a paragraph.
        Assert.Contains("placeholder", html, StringComparison.Ordinal);
    }

    [Fact]
    public void One_bad_declaration_does_not_take_the_good_ones_with_it()
    {
        var html = EmailHtml.Sanitize(
            "<p style=\"color: #ffffff; position: absolute; padding: 4px\">x</p>");

        Assert.Contains("color: #ffffff;", html, StringComparison.Ordinal);
        Assert.Contains("padding: 4px;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("position", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_entity_encoded_property_is_read_as_the_word_it_becomes()
    {
        var html = EmailHtml.Sanitize(
            "<p style=\"width: &#101;xpression(alert(1))\">placeholder</p>");

        Assert.DoesNotContain("xpression", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Important_is_the_only_bang_a_value_may_carry()
    {
        var html = EmailHtml.Sanitize(
            "<p style=\"color: red !important; padding: 1px ! nonsense\">x</p>");

        Assert.Contains("color: red !important;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("nonsense", html, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------- text part ---

    [Fact]
    public void The_text_part_of_an_html_body_reads_as_prose()
    {
        var text = EmailText.From(EmailHtml.Sanitize("""
            <table role="presentation" width="100%">
              <tr>
                <td style="padding: 8px">
                  <h1>Placeholder heading</h1>
                  <p>One placeholder line.<br>Another placeholder line.</p>
                  <ul><li>one</li><li>two</li></ul>
                  <a href="https://example.invalid/go">placeholder link</a>
                </td>
              </tr>
            </table>
            """));

        Assert.Contains("Placeholder heading", text, StringComparison.Ordinal);
        Assert.Contains("One placeholder line.\nAnother placeholder line.", text, StringComparison.Ordinal);
        Assert.Contains("- one", text, StringComparison.Ordinal);
        Assert.Contains("- two", text, StringComparison.Ordinal);

        // The URL survives as something somebody can copy, which is the whole
        // reason a text part is worth having.
        Assert.Contains(
            "placeholder link <https://example.invalid/go>", text, StringComparison.Ordinal);

        Assert.DoesNotContain("<td", text, StringComparison.Ordinal);
        Assert.DoesNotContain("padding", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_text_part_keeps_placeholders_where_the_renderer_can_find_them()
    {
        var text = EmailText.From("<p>Hello {{firstName}}</p><a href=\"{{link}}\">Sign in</a>");

        Assert.Contains("{{firstName}}", text, StringComparison.Ordinal);
        Assert.Contains("{{link}}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_text_part_never_prints_the_body_of_a_script()
    {
        var text = EmailText.From("<p>placeholder</p><script>alert(1)</script>");

        Assert.DoesNotContain("alert", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("placeholder", text);
    }

    [Fact]
    public void The_text_part_collapses_the_whitespace_html_is_made_of()
    {
        var text = EmailText.From("""
            <div>

                  <p>   one    placeholder   </p>


                  <p>two</p>
            </div>
            """);

        Assert.Equal("one placeholder\n\ntwo", text);
    }

    [Fact]
    public void A_stylesheet_in_the_head_survives_the_head_being_unwrapped()
    {
        // The bug behind all of this. `head` was discarded wholesale, so the
        // branch that reads a stylesheet was never reached, and a designed
        // email lost every margin it had — which showed up as gaps nobody put
        // there rather than as anything an author could see was missing.
        var html = EmailHtml.Sanitize(
            "<!doctype html><html><head><title>x</title>"
            + "<style>p{margin:0 0 12px}</style></head>"
            + "<body><p>Hello.</p></body></html>");

        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.Contains("margin: 0 0 12px", html, StringComparison.Ordinal);
        Assert.Contains("<p>Hello.</p>", html, StringComparison.Ordinal);

        // The title goes with its text, as it always did. Unwrapping the head
        // must not start printing what was in the tab.
        Assert.DoesNotContain("x<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_script_in_the_head_still_goes_with_its_body()
    {
        // The head is unwrapped now, which would be a way in if anything else
        // on the discarded list stopped being checked on its own.
        var html = EmailHtml.Sanitize(
            "<html><head><script>alert(1)</script></head><body><p>Hi.</p></body></html>");

        Assert.DoesNotContain("alert", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Hi.</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_whole_document_comes_back_without_blank_lines_above_it()
    {
        // A doctype and a head leave their whitespace behind once they are
        // gone. It renders as empty space above the email and appears in no
        // editor the author checked it in.
        var html = EmailHtml.Sanitize(
            "<!doctype html>\n<html>\n<head>\n<title>x</title>\n</head>\n"
            + "<body>\n<p>Hello.</p>\n</body>\n</html>");

        Assert.StartsWith("<p>", html, StringComparison.Ordinal);
        Assert.EndsWith("</p>", html, StringComparison.Ordinal);
    }
}
