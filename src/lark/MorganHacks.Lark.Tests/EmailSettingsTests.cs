using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Tests;

public class EmailSettingsTests(NotifyDatabase db) : IClassFixture<NotifyDatabase>
{
    [Fact]
    public void Preview_text_is_personalized_escaped_and_hidden_inside_the_body()
    {
        var template = new EmailTemplate(Guid.NewGuid(), "test", "broadcast", "Subject",
            "<html><head><title>Email</title></head><body><p>Body</p></body></html>",
            "Body", "mail", "example.invalid", null,
            PreviewText: "Hello {{name}} <b>preview</b>");
        var rendered = TemplateRenderer.Render(template, new Dictionary<string, string>
        {
            ["name"] = "Ada & Grace",
        });

        Assert.Contains("<body><div aria-hidden=\"true\"", rendered.BodyHtml);
        Assert.Contains("display:none", rendered.BodyHtml);
        Assert.Contains("Hello Ada &amp; Grace &lt;b&gt;preview&lt;/b&gt;</div><p>Body</p>", rendered.BodyHtml);
        Assert.Equal("Body", rendered.BodyText);
        Assert.Contains("name", TemplateRenderer.PlaceholdersIn(template));
        Assert.Equal(template.BodyHtml, EmailPreheader.Add(template.BodyHtml, " "));
    }

    [Fact]
    public async Task Tracking_is_opt_in_and_retry_links_keep_the_same_destination()
    {
        var campaign = await db.AddCampaignAsync();
        var id = await db.QueueAsync(campaign, $"tracking-{Guid.NewGuid():N}@example.invalid");
        const string url = "https://example.invalid/go?a=1&b=2#details";
        const string html = "<a href='https://example.invalid/go?a=1&amp;b=2#details' title='a > b'>Go</a>"
            + "<a href='mailto:hello@example.invalid'>Reply</a><a href='#local'>Local</a>"
            + "<img src='https://example.invalid/pic.png'>";
        var message = new ClaimedMessage(id, campaign, "test@example.invalid", 10, 0,
            "Subject", html, $"Go <{url}> and {url}/longer", "mail@example.invalid", null, null);
        var store = new LinkTrackingStore(db.DataSource);
        Assert.Equal(message, await store.PrepareAsync(message, ""));

        var first = await store.PrepareAsync(message with { ClickTracking = true }, "https://api.example.invalid/api");
        var retry = await store.PrepareAsync(message with { ClickTracking = true }, "https://api.example.invalid/api");
        Assert.Equal(first, retry);
        Assert.Contains("https://api.example.invalid/api/email/click/", first.BodyHtml);
        Assert.Contains("mailto:hello@example.invalid", first.BodyHtml);
        Assert.Contains("href='#local'", first.BodyHtml);
        Assert.Contains("https://example.invalid/pic.png", first.BodyHtml);
        Assert.Contains(url + "/longer", first.BodyText);

        await using var command = db.DataSource.CreateCommand("SELECT id, destination, click_count FROM notify.tracked_links WHERE message_id = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var token = reader.GetGuid(0);
        Assert.Equal(url, reader.GetString(1));
        Assert.Equal(0, reader.GetInt64(2));
        Assert.Contains(token.ToString("N"), first.BodyText);
        Assert.False(await reader.ReadAsync());
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//example.invalid/path")]
    [InlineData("https://name:password@example.invalid")]
    [InlineData("https://example.invalid/\r\nLocation: bad")]
    public void Non_web_and_unsafe_links_are_not_tracked(string value)
    {
        Assert.False(EmailLinks.IsWebUrl(value));
    }

    [Fact]
    public void Links_in_comments_and_styles_are_not_rewritten()
    {
        var html = "<!-- <a href='https://comment.invalid'> -->"
            + "<style>a:after { content: \"<a href='https://style.invalid'>\"; }</style>"
            + "<a href='https://real.invalid'>Real</a>";
        Assert.Equal(["https://real.invalid"], EmailLinks.Destinations(html));
    }
}
