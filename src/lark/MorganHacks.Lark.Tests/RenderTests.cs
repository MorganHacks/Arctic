using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Tests;

/// <summary>Filling a template, which happens once and is then frozen.</summary>
public class RenderTests
{
    private static EmailTemplate Template(string subject, string html, string text) =>
        new(Guid.NewGuid(), "test", "transactional", subject, html, text,
            "login", "auth.example.com", null);

    [Fact]
    public void Values_are_escaped_in_the_html_part()
    {
        // These values are whatever a person typed into a form. An email
        // client is a browser, so dropping them into HTML unescaped is the
        // same bug as rendering them into a page unescaped.
        var rendered = TemplateRenderer.Render(
            Template("hi", "<p>Hello {{name}}</p>", "Hello {{name}}"),
            new Dictionary<string, string> { ["name"] = "<script>alert(1)</script>" });

        Assert.DoesNotContain("<script>", rendered.BodyHtml);
        Assert.Contains("&lt;script&gt;", rendered.BodyHtml);
    }

    [Fact]
    public void The_text_part_is_left_alone()
    {
        // There is nothing to escape into in a text part, and escaping it
        // would show the reader literal entity codes.
        var rendered = TemplateRenderer.Render(
            Template("hi", "<p>{{name}}</p>", "Hello {{name}}"),
            new Dictionary<string, string> { ["name"] = "Ada & Grace" });

        Assert.Contains("Ada & Grace", rendered.BodyText);
    }

    [Fact]
    public void A_missing_value_stays_visible_rather_than_vanishing()
    {
        // An empty substitution reads as a sentence with a hole in it that
        // nobody notices. Leaving the placeholder makes the mistake obvious.
        var rendered = TemplateRenderer.Render(
            Template("hi", "<p>{{missing}}</p>", "{{missing}}"), new Dictionary<string, string>());

        Assert.Contains("{{missing}}", rendered.BodyText);
    }

    [Fact]
    public void A_link_survives_rendering_intact()
    {
        var link = "https://morganhacks.com/auth/consume?token=abc-123_XYZ";

        var rendered = TemplateRenderer.Render(
            Template("hi", "<a href=\"{{link}}\">Sign in</a>", "{{link}}"),
            new Dictionary<string, string> { ["link"] = link });

        Assert.Contains(link, rendered.BodyText);
    }

    [Fact]
    public void A_transactional_template_outranks_a_broadcast_one()
    {
        var login = Template("hi", "h", "t");
        var blast = login with { Kind = "broadcast" };

        Assert.True(login.Priority < blast.Priority);
    }
}
