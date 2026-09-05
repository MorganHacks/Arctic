using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Tests;

/// <summary>
/// What the author is told before they send.
/// </summary>
/// <remarks>
/// Bodies here are deliberate nonsense. Template wording belongs to the people
/// who write templates, and a test fixture is not a sentence somebody should
/// end up copying into production.
/// </remarks>
public class EmailHtmlNotesTests
{
    private static IReadOnlyList<string> Notes(string source) =>
        EmailHtmlNotes.For("html", source);

    [Fact]
    public void A_style_block_is_reported()
    {
        var notes = Notes("<style>.a{color:red}</style><p>placeholder</p>");

        Assert.Contains(notes, n => n.Contains("<style>", StringComparison.Ordinal));
    }

    [Fact]
    public void The_note_says_what_to_do_instead()
    {
        // A warning that names the problem and not the fix gets read once.
        var notes = Notes("<style>.a{color:red}</style><p>placeholder</p>");

        Assert.Contains(notes, n => n.Contains("inline", StringComparison.Ordinal));
    }

    [Fact]
    public void Dead_classes_are_only_mentioned_once_the_rules_have_gone()
    {
        // A class on its own is harmless and saying so is noise. After the
        // stylesheet has been removed it is the reason the layout collapsed.
        var withRules = Notes("<style>.a{color:red}</style><p class=\"a\">placeholder</p>");
        var withoutRules = Notes("<p class=\"a\">placeholder</p>");

        Assert.Contains(withRules, n => n.Contains("class attributes", StringComparison.Ordinal));
        Assert.Empty(withoutRules);
    }

    [Fact]
    public void A_linked_stylesheet_is_reported()
    {
        var notes = Notes("<link rel=\"stylesheet\" href=\"https://example.invalid/a.css\"><p>x</p>");

        Assert.Contains(notes, n => n.Contains("stylesheet", StringComparison.Ordinal));
    }

    [Fact]
    public void Several_blocks_are_counted_rather_than_repeated()
    {
        var notes = Notes("<style>a{}</style><style>b{}</style><p>placeholder</p>");

        Assert.Contains(notes, n => n.Contains("All 2", StringComparison.Ordinal));
        Assert.Single(notes, n => n.Contains("style", StringComparison.Ordinal));
    }

    [Fact]
    public void Clean_email_html_gets_no_notes()
    {
        // The control. Notes on everything are notes on nothing: if a correctly
        // written template also collected a warning, the warning would be
        // dismissed on sight and the one that matters with it.
        var notes = Notes(
            "<table role=\"presentation\" width=\"600\"><tbody><tr>"
            + "<td style=\"padding: 40px; background-color: #101010\">placeholder</td>"
            + "</tr></tbody></table>");

        Assert.Empty(notes);
    }

    [Fact]
    public void Markdown_is_not_lectured_about_html_it_did_not_write()
    {
        // The dialect has no way to write a style block, so the note could only
        // ever fire on prose that happened to mention one.
        Assert.Empty(EmailHtmlNotes.For("markdown", "A paragraph about <style> blocks."));
    }

    [Fact]
    public void Nothing_typed_yet_is_not_a_problem_yet()
    {
        // The editor calls this on a keystroke.
        Assert.Empty(EmailHtmlNotes.For("html", null));
        Assert.Empty(EmailHtmlNotes.For("html", "   "));
    }
}
