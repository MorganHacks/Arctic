using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;
using Npgsql;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Writing the templates mail is sent from.
/// </summary>
/// <remarks>
/// Against a real database, because the two rules worth most here are enforced
/// by it: the partial unique index that says a key has one live version, and
/// the trigger that says a template's kind never changes. A hand-rolled schema
/// would pass these whether or not either exists.
/// <para>
/// Every subject and body below is deliberate nonsense. Template wording
/// belongs to the people who send the mail, and a plausible sentence in a test
/// file is a sentence somebody eventually copies into production.
/// </para>
/// </remarks>
public class TemplateTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    // --------------------------------------------------------------- safety ---

    [Fact]
    public async Task A_script_tag_does_not_survive_being_saved()
    {
        // The whole reason the body is not stored as typed. A template is
        // written once and mailed to several hundred people, and a tracking
        // script that came along with a pasted newsletter must not be one of
        // the things that goes with it.
        var (_, cookie) = await Comms();

        var saved = await Body(await Post(cookie, Draft(Key(), markdown:
            "First line.\n\n<script>alert('placeholder')</script>\n\nSecond line.")));

        var html = saved.GetProperty("html").GetString()!;

        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("First line.", html, StringComparison.Ordinal);

        // And not in the text part either, which is derived from the same
        // source and would otherwise print the script as if it were prose.
        Assert.DoesNotContain(
            "alert", saved.GetProperty("text").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_event_handler_does_not_survive_being_saved()
    {
        // onerror is the one that arrives by accident: it is what a WYSIWYG
        // editor leaves on an image, so nobody typed it and nobody sees it in
        // the source they pasted.
        var (_, cookie) = await Comms();

        // With a line of prose beside it, because a body that is only a picture
        // has no text part at all and is refused for that instead.
        var saved = await Body(await Post(cookie, Draft(Key(), markdown:
            "Placeholder line.\n\n"
            + "<img src=\"https://example.invalid/a.png\" onerror=\"alert('x')\" alt=\"pic\">")));

        var html = saved.GetProperty("html").GetString()!;

        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", html, StringComparison.OrdinalIgnoreCase);

        // The image itself is fine and stays. Stripping the attribute rather
        // than the picture is the difference between a sanitiser and a wall.
        Assert.Contains("<img", html, StringComparison.Ordinal);
        Assert.Contains("https://example.invalid/a.png", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"pic\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_javascript_url_is_not_a_link()
    {
        var (_, cookie) = await Comms();

        var saved = await Body(await Post(cookie, Draft(Key(), markdown:
            "[placeholder one](javascript:alert)\n\n"
            + "<a href=\"&#106;avascript:alert\">placeholder two</a>\n\n"
            + "[placeholder three](https://example.invalid/ok)")));

        var html = saved.GetProperty("html").GetString()!;

        // Including the entity-encoded spelling, which is the same scheme by
        // the time a browser reads it.
        Assert.DoesNotContain("javascript", html, StringComparison.OrdinalIgnoreCase);

        // The words survive; only the link does not. Somebody proofreading
        // should still be able to read their own sentence.
        Assert.Contains("placeholder one", html, StringComparison.Ordinal);
        Assert.Contains("placeholder two", html, StringComparison.Ordinal);

        // A real link is untouched.
        Assert.Contains(
            "<a href=\"https://example.invalid/ok\">placeholder three</a>",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inline_styling_is_kept_and_a_style_block_is_not()
    {
        // This assertion used to run the other way: nothing styled survived at
        // all. That was wrong about email rather than careful about safety —
        // inline CSS works in essentially every client and is how all
        // marketing mail is built, so refusing it meant no template could
        // contain a button.
        //
        // The block is still discarded, and that is a different judgement:
        // Gmail drops a <style> block when a message is forwarded, so a
        // template that depends on one looks right until the first person
        // passes it on. Every layout that needs it can be written inline.
        var (_, cookie) = await Comms();

        var saved = await Body(await Post(cookie, Draft(Key(), markdown:
            "<style>p { color: red }</style>\n\n"
            + "<div class=\"wrapper\" style=\"color: #123456\">placeholder</div>")));

        var html = saved.GetProperty("html").GetString()!;

        Assert.Contains("style=\"color: #123456;\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"wrapper\"", html, StringComparison.Ordinal);

        // The block's contents are gone, rather than printed as prose.
        Assert.DoesNotContain("p { color: red }", html, StringComparison.Ordinal);
        Assert.Contains("placeholder", html, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------- html ---

    [Fact]
    public async Task An_html_template_round_trips()
    {
        // The source comes back as it was typed, under the format that says
        // how to read it. An editor that reopened a template as generated
        // markup would be the round trip through generated HTML that storing a
        // source exists to remove.
        var (_, cookie) = await Comms();
        var key = Key();

        var source =
            "<div><h1>Placeholder heading</h1><p>A placeholder line.</p></div>";

        var created = await Body(await Post(cookie, Html(key, body: source)));

        Assert.Equal("html", created.GetProperty("format").GetString());
        Assert.Equal(source, created.GetProperty("body").GetString());

        // And null under the old name, rather than HTML in a field a caller
        // that has not heard about formats would put in a Markdown editor.
        Assert.Equal(JsonValueKind.Null, created.GetProperty("markdown").ValueKind);

        var read = await Body(await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/templates/{key}", cookie)));

        Assert.Equal("html", read.GetProperty("format").GetString());
        Assert.Equal(source, read.GetProperty("body").GetString());
        Assert.Contains("<h1>Placeholder heading</h1>",
            read.GetProperty("html").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_table_and_its_inline_styles_survive_an_html_template()
    {
        // A button in email is a one-cell table with a background colour.
        // Outlook's flexbox and grid support is bad enough that this is still
        // the layout mechanism, and cellpadding is honoured where a stylesheet
        // is not — so the deprecated attributes are the ones that work.
        var (_, cookie) = await Comms();

        var saved = await Body(await Post(cookie, Html(Key(), body:
            "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" "
            + "cellspacing=\"0\" border=\"0\" bgcolor=\"#ffffff\">"
            + "<tbody><tr><td align=\"center\" valign=\"middle\" colspan=\"2\" "
            + "style=\"padding: 12px 24px; background-color: #101010; "
            + "border-radius: 4px\">"
            + "<a href=\"https://example.invalid/go\" "
            + "style=\"color: #ffffff; text-decoration: none\">placeholder</a>"
            + "</td></tr></tbody></table>")));

        var html = saved.GetProperty("html").GetString()!;

        foreach (var kept in new[]
        {
            "<table", "<tbody>", "<tr>", "<td", "role=\"presentation\"",
            "width=\"100%\"", "cellpadding=\"0\"", "cellspacing=\"0\"",
            "border=\"0\"", "bgcolor=\"#ffffff\"", "align=\"center\"",
            "valign=\"middle\"", "colspan=\"2\"", "padding: 12px 24px;",
            "background-color: #101010;", "border-radius: 4px;",
            "text-decoration: none;", "https://example.invalid/go",
        })
        {
            Assert.Contains(kept, html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_html_template_is_sanitised_the_same_way_a_markdown_one_is()
    {
        // Same allow-list, same refusals. A second sanitiser for the second
        // language would agree with the first until somebody changed one.
        var (_, cookie) = await Comms();

        var saved = await Body(await Post(cookie, Html(Key(), body:
            "<p>placeholder one</p>"
            + "<script>alert('one')</script>"
            + "<iframe src=\"https://example.invalid/\"></iframe>"
            + "<img src=\"https://example.invalid/a.png\" alt=\"pic\" "
            + "onerror=\"alert('two')\">"
            + "<a href=\"javascript:alert('three')\">placeholder two</a>")));

        var html = saved.GetProperty("html").GetString()!;

        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", html, StringComparison.OrdinalIgnoreCase);

        // What was safe about each of them is still there.
        Assert.Contains("placeholder one", html, StringComparison.Ordinal);
        Assert.Contains("placeholder two", html, StringComparison.Ordinal);
        Assert.Contains("<img src=\"https://example.invalid/a.png\"", html,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_style_value_that_could_execute_does_not_survive()
    {
        // The reason the style attribute is read rather than trusted.
        // expression() is old IE's way of putting script in a stylesheet, and
        // CSS has a comment syntax specifically good at spelling it in a way a
        // substring check misses.
        var (_, cookie) = await Comms();

        var saved = await Body(await Post(cookie, Html(Key(), body:
            "<p style=\"width: expression(alert(1)); color: #010203\">placeholder</p>"
            + "<p style=\"width: expr/**/ession(alert(2))\">placeholder two</p>"
            + "<p style=\"background: url(javascript:alert(3))\">placeholder three</p>")));

        var html = saved.GetProperty("html").GetString()!;

        Assert.DoesNotContain("expression", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", html, StringComparison.OrdinalIgnoreCase);

        // One refused declaration does not take the paragraph or the colour
        // beside it. An author who asked for one thing this cannot vouch for
        // has still written a paragraph.
        Assert.Contains("color: #010203;", html, StringComparison.Ordinal);
        Assert.Contains("placeholder three", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_html_template_gets_a_text_part_too()
    {
        // 0003 requires body_text and says why: text-only clients exist, and a
        // message with no text part scores worse with spam filters. An HTML
        // template has no prose source to derive it from, so it is read back
        // out of the layout.
        var (_, cookie) = await Comms();

        var saved = await Body(await Post(cookie, Html(Key(), body:
            "<table><tr><td style=\"padding: 8px\">"
            + "<h1>Placeholder heading</h1>"
            + "<p>One placeholder line.<br>Another placeholder line.</p>"
            + "<ul><li>one</li><li>two</li></ul>"
            + "<a href=\"https://example.invalid/go\">placeholder link</a>"
            + "</td></tr></table>")));

        var text = saved.GetProperty("text").GetString()!;

        Assert.Contains("Placeholder heading", text, StringComparison.Ordinal);
        Assert.Contains(
            "One placeholder line.\nAnother placeholder line.",
            text,
            StringComparison.Ordinal);
        Assert.Contains("- one", text, StringComparison.Ordinal);

        // The URL survives as something somebody can copy, which is the whole
        // reason a text part is worth having.
        Assert.Contains(
            "placeholder link <https://example.invalid/go>", text, StringComparison.Ordinal);

        // And none of the layout does.
        Assert.DoesNotContain("<td", text, StringComparison.Ordinal);
        Assert.DoesNotContain("padding", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_template_is_markdown_unless_it_says_otherwise()
    {
        // Every caller written before there was a choice sends no format and
        // means markdown, so a request without one has to keep working exactly
        // as it did.
        var (_, cookie) = await Comms();

        var saved = await Body(await Post(cookie, Draft(Key(), markdown: "**placeholder**")));

        Assert.Equal("markdown", saved.GetProperty("format").GetString());
        Assert.Contains(
            "<strong>placeholder</strong>",
            saved.GetProperty("html").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_format_that_is_not_one_of_the_two_is_refused()
    {
        // Rather than quietly treated as the default. A caller that asked for
        // something else and got a markdown render back would find out from
        // the mail.
        var (_, cookie) = await Comms();

        var response = await Post(cookie, Html(Key(), body: "<p>x</p>", format: "mdx"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_template_saved_as_markdown_can_be_saved_again_as_html()
    {
        // Converting is a thing an author does once the layout outgrows the
        // dialect, and unlike kind it changes nothing about which queue the
        // message joins or which subdomain it leaves from.
        var (_, cookie) = await Comms();
        var key = await Existing();

        var saved = await Body(await Client().SendAsync(Request(
            HttpMethod.Put,
            $"/admin/templates/{key}",
            cookie,
            Html(key, body: "<p>Second placeholder body.</p>"))));

        Assert.Equal("html", saved.GetProperty("format").GetString());
        Assert.Equal(2, saved.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task A_body_that_sanitises_away_to_nothing_is_refused()
    {
        // body_html and body_text are both NOT NULL, and an empty string in
        // them is a template that sends a blank email rather than one that
        // fails loudly.
        var (_, cookie) = await Comms();

        var response = await Post(cookie, Draft(Key(), markdown: "<script>alert(1)</script>"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------------------------------------------- markdown ---

    [Fact]
    public async Task Markdown_becomes_both_bodies()
    {
        // 0003 requires both columns and says why: text-only clients exist and
        // a text part improves the spam score. Both come from one source so
        // they cannot drift apart.
        var (_, cookie) = await Comms();

        var saved = await Body(await Post(cookie, Draft(Key(), markdown:
            "# Placeholder heading\n\n"
            + "A **bold** placeholder with a [link](https://example.invalid/x).\n\n"
            + "- one\n- two\n")));

        var html = saved.GetProperty("html").GetString()!;
        var text = saved.GetProperty("text").GetString()!;

        Assert.Contains("<h1>Placeholder heading</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>bold</strong>", html, StringComparison.Ordinal);
        Assert.Contains(
            "<a href=\"https://example.invalid/x\">link</a>", html, StringComparison.Ordinal);
        Assert.Contains("<ul><li>one</li><li>two</li></ul>", html, StringComparison.Ordinal);

        // The text part keeps the URL rather than the markup, because the only
        // reason to read it is that the HTML did not arrive.
        Assert.Contains("Placeholder heading", text, StringComparison.Ordinal);
        Assert.Contains("link <https://example.invalid/x>", text, StringComparison.Ordinal);
        Assert.Contains("- one", text, StringComparison.Ordinal);
        Assert.DoesNotContain("**", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", text, StringComparison.Ordinal);

        // And the source survives, which is the point of storing it: a template
        // that can only be edited as generated HTML is one nobody edits twice.
        Assert.Contains(
            "# Placeholder heading",
            saved.GetProperty("markdown").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Placeholders_are_discovered_and_handed_back()
    {
        // So the console can say which values a template needs before somebody
        // aims it at a segment that cannot supply them.
        var (_, cookie) = await Comms();

        var saved = await Body(await Post(
            cookie,
            Draft(Key(), subject: "Placeholder for {{firstName}}", markdown:
                "Hello {{firstName}}, we have {{email}} on file.")));

        var found = saved.GetProperty("placeholders")
                         .EnumerateArray()
                         .Select(placeholder => placeholder.GetString() ?? string.Empty)
                         .ToArray();

        Assert.Equal(["email", "firstName"], found);
    }

    // ------------------------------------------------------------ previewing ---

    [Fact]
    public async Task A_preview_renders_a_template_that_does_not_exist()
    {
        // The editor calls this while somebody is typing, so there is nothing
        // saved to read and nothing may be written.
        var (_, cookie) = await Comms();

        var before = await TemplateCount();

        var preview = await Body(await Client().SendAsync(Request(
            HttpMethod.Post,
            "/admin/templates/preview",
            cookie,
            new { subject = "Placeholder {{firstName}}", markdown = "Hello {{firstName}}." })));

        Assert.Equal("Placeholder {{firstName}}", preview.GetProperty("subject").GetString());
        Assert.Contains(
            "Hello {{firstName}}.",
            preview.GetProperty("html").GetString()!,
            StringComparison.Ordinal);

        Assert.Equal(before, await TemplateCount());
    }

    [Fact]
    public async Task A_preview_fills_only_the_values_it_is_given()
    {
        // The same behaviour TemplateRenderer has at queue time. A placeholder
        // with no value stands rather than emptying, so somebody notices it on
        // this screen instead of in four hundred inboxes.
        var (_, cookie) = await Comms();

        var preview = await Body(await Client().SendAsync(Request(
            HttpMethod.Post,
            "/admin/templates/preview",
            cookie,
            new
            {
                subject = "Placeholder",
                markdown = "Hello {{firstName}} of {{school}}.",
                values = new { firstName = "Ada" },
            })));

        var html = preview.GetProperty("html").GetString()!;

        Assert.Contains("Hello Ada", html, StringComparison.Ordinal);
        Assert.Contains("{{school}}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_preview_is_sanitised_the_same_way_a_save_is()
    {
        // A preview that agreed with the editor and disagreed with the send
        // would be worse than no preview, because it would be believed.
        var (_, cookie) = await Comms();

        var preview = await Body(await Client().SendAsync(Request(
            HttpMethod.Post,
            "/admin/templates/preview",
            cookie,
            new { subject = "Placeholder", markdown = "<script>alert(1)</script>ok" })));

        Assert.DoesNotContain(
            "alert",
            preview.GetProperty("html").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    // ----------------------------------------------------------- permissions ---

    [Fact]
    public async Task Everything_here_needs_email_manage_templates()
    {
        // Reads as well as writes. A template is not a record of something that
        // happened — it is the thing that will be sent — so there is no useful
        // narrower reader, and email.view_stats is the permission for "did that
        // go out".
        var reader = await db.AddPersonAsync(Unique("stats"));
        await db.GrantAsync(reader, "email.view_stats");
        var cookie = await SignIn(reader);

        var key = await Existing();

        foreach (var request in new[]
        {
            Request(HttpMethod.Get, "/admin/templates", cookie),
            Request(HttpMethod.Get, $"/admin/templates/{key}", cookie),
            Request(HttpMethod.Post, "/admin/templates", cookie, Draft(Key())),
            Request(HttpMethod.Put, $"/admin/templates/{key}", cookie, Draft(key)),
            Request(HttpMethod.Post, "/admin/templates/preview", cookie,
                new { subject = "Placeholder", markdown = "placeholder" }),
        })
        {
            var response = await Client().SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    // ------------------------------------------------------------------- kind ---

    [Fact]
    public async Task A_templates_kind_cannot_change()
    {
        // kind decides the queue lane and the sending subdomain. A broadcast
        // template turned transactional starts jumping the queue ahead of
        // sign-in links; the reverse puts every sign-in link behind whatever
        // announcement is draining, from the domain that collects the spam
        // complaints.
        var (_, cookie) = await Comms();
        var key = await Existing();

        var response = await Client().SendAsync(Request(
            HttpMethod.Put,
            $"/admin/templates/{key}",
            cookie,
            Draft(key, kind: "transactional")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // And the template is untouched: a refused save must not have retired
        // the version it refused to replace.
        var still = await Body(await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/templates/{key}", cookie)));

        Assert.Equal("broadcast", still.GetProperty("kind").GetString());
        Assert.Equal(1, still.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task The_database_refuses_a_kind_change_too()
    {
        // Templates are still created by hand-written SQL, so the rule has to
        // be true of the table rather than true of one handler.
        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE notify.templates SET kind = 'broadcast' WHERE key = 'magic_link'");

        var refused = await Assert.ThrowsAsync<PostgresException>(
            async () => await cmd.ExecuteNonQueryAsync());

        Assert.Contains("kind is fixed", refused.MessageText, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- saving ---

    [Fact]
    public async Task Saving_writes_a_new_version_and_leaves_the_old_one_alone()
    {
        var (_, cookie) = await Comms();
        var key = await Existing();

        var first = await Body(await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/templates/{key}", cookie)));

        var second = await Body(await Client().SendAsync(Request(
            HttpMethod.Put,
            $"/admin/templates/{key}",
            cookie,
            Draft(key, subject: "Placeholder two", markdown: "Second placeholder body."))));

        Assert.Equal(1, first.GetProperty("version").GetInt32());
        Assert.Equal(2, second.GetProperty("version").GetInt32());

        // Two rows, one of them retired. The retired one is what campaigns
        // already sent, and it is not deleted.
        Assert.Equal(2, await RowsFor(key));
        Assert.Equal(1, await LiveRowsFor(key));

        // And the key still resolves to the new one for anything looking it up
        // by name.
        var current = await Body(await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/templates/{key}", cookie)));

        Assert.Equal("Placeholder two", current.GetProperty("subject").GetString());
        Assert.Equal(2, current.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task A_sent_campaign_still_points_at_what_it_sent()
    {
        // The decision this whole change turns on. History has to keep saying
        // what happened, and campaigns.template_id is a foreign key to a
        // specific row: overwriting that row would make every sent campaign
        // report today's wording as the wording it sent.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();

        var key = await Existing();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("recipient"), "accepted");

        var campaign = await Campaign(drafting, key, eventId);
        Assert.Equal(HttpStatusCode.OK, (await Send(campaign, sending)).StatusCode);

        await Client().SendAsync(Request(
            HttpMethod.Put,
            $"/admin/templates/{key}",
            drafting,
            Draft(key, subject: "Placeholder two", markdown: "Second placeholder body.")));

        var (subject, html) = await TemplateBehind(campaign);

        Assert.Equal("Placeholder one", subject);
        Assert.Contains("First placeholder body.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_drafted_campaign_cannot_be_sent_after_its_template_changes()
    {
        // The price of copying on write, paid deliberately. An approver signs
        // off on a template and a segment together, and a broadcast cannot be
        // recalled — so a campaign approved against wording that has since
        // changed has to be drafted again. CampaignEndpoints already refuses on
        // exactly this condition; until now nothing could make it true.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();

        var key = await Existing();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("recipient"), "accepted");

        var campaign = await Campaign(drafting, key, eventId);

        await Client().SendAsync(Request(
            HttpMethod.Put,
            $"/admin/templates/{key}",
            drafting,
            Draft(key, subject: "Placeholder two", markdown: "Second placeholder body.")));

        var response = await Send(campaign, sending);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "has changed",
            (await Body(response)).GetProperty("error").GetString()!,
            StringComparison.Ordinal);

        // And nothing was queued, which is the part that matters.
        Assert.Equal(0, await MessageCount(campaign));
    }

    [Fact]
    public async Task A_key_cannot_be_used_twice()
    {
        var (_, cookie) = await Comms();
        var key = await Existing();

        var response = await Post(cookie, Draft(key));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await LiveRowsFor(key));
    }

    [Fact]
    public async Task A_key_cannot_be_changed_by_saving()
    {
        var (_, cookie) = await Comms();
        var key = await Existing();

        var response = await Client().SendAsync(Request(
            HttpMethod.Put, $"/admin/templates/{key}", cookie, Draft("something-else")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Saving_a_template_that_does_not_exist_says_so()
    {
        var (_, cookie) = await Comms();

        var response = await Client().SendAsync(Request(
            HttpMethod.Put, "/admin/templates/nothing-here", cookie, Draft("nothing-here")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_list_shows_one_row_per_template_however_often_it_was_edited()
    {
        var (_, cookie) = await Comms();
        var key = await Existing();

        await Client().SendAsync(Request(
            HttpMethod.Put,
            $"/admin/templates/{key}",
            cookie,
            Draft(key, subject: "Placeholder two", markdown: "Second placeholder body.")));

        var listed = (await Body(await Client().SendAsync(
                Request(HttpMethod.Get, "/admin/templates", cookie))))
            .GetProperty("templates")
            .EnumerateArray()
            .Where(template => template.GetProperty("key").GetString() == key)
            .ToArray();

        Assert.Single(listed);
        Assert.Equal(2, listed[0].GetProperty("version").GetInt32());
        Assert.Equal("Placeholder two", listed[0].GetProperty("subject").GetString());

        // No bodies on the list. This is the screen somebody opens to pick
        // which template to edit.
        Assert.False(listed[0].TryGetProperty("markdown", out _));
    }

    [Fact]
    public async Task A_new_broadcast_template_can_actually_be_broadcast()
    {
        // The end of the story this change starts from: before this endpoint
        // there was exactly one template, it was transactional, and so no mass
        // mail could be sent at all.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();

        var key = await Existing();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("recipient"), "accepted");

        var campaign = await Campaign(drafting, key, eventId);
        var response = await Send(campaign, sending);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await Body(response)).GetProperty("queued").GetInt32());
    }

    // --------------------------------------------------------------- fixtures ---

    private HttpClient Client() => _app.CreateClient();

    private async Task<(Guid Person, string Cookie)> Comms()
    {
        var id = await db.AddPersonAsync(Unique("comms"));
        await db.AddToTeamAsync(id, "comms");
        return (id, await SignIn(id));
    }

    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private static string Key() => $"placeholder-{Guid.NewGuid():N}";

    /// <summary>A saved broadcast template, with deliberately meaningless copy.</summary>
    private async Task<string> Existing()
    {
        var (_, cookie) = await Comms();
        var key = Key();

        var response = await Post(cookie, Draft(key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return key;
    }

    private static object Draft(
        string key,
        string kind = "broadcast",
        string subject = "Placeholder one",
        string markdown = "First placeholder body.") => new
        {
            key,
            kind,
            subject,
            markdown,
            fromLocal = "news",
            fromDomain = "news.example.invalid",
            replyTo = (string?)null,
        };

    /// <summary>The same, written in HTML rather than in Markdown.</summary>
    /// <remarks>
    /// Sends the source as <c>body</c> rather than as <c>markdown</c>, which is
    /// the name it has now that there are two languages. The older name is
    /// still accepted and is what <see cref="Draft"/> above sends, so both are
    /// exercised.
    /// </remarks>
    private static object Html(
        string key,
        string body,
        string format = "html",
        string kind = "broadcast",
        string subject = "Placeholder one") => new
        {
            key,
            kind,
            subject,
            format,
            body,
            fromLocal = "news",
            fromDomain = "news.example.invalid",
            replyTo = (string?)null,
        };

    private Task<HttpResponseMessage> Post(string cookie, object body) =>
        Client().SendAsync(Request(HttpMethod.Post, "/admin/templates", cookie, body));

    private static HttpRequestMessage Request(
        HttpMethod method, string path, string cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    // -------------------------------------------------------------- campaigns ---

    private async Task<Guid> Campaign(string cookie, string templateKey, Guid eventId)
    {
        var response = await Client().SendAsync(Request(
            HttpMethod.Post,
            "/admin/campaigns",
            cookie,
            new
            {
                name = "A campaign",
                templateKey,
                segment = new
                {
                    type = "applicationStatus",
                    eventId,
                    statuses = new[] { "accepted" },
                },
            }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Body(response)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Send(Guid campaignId, string cookie) =>
        Client().SendAsync(
            Request(HttpMethod.Post, $"/admin/campaigns/{campaignId}/send", cookie));

    /// <summary>Fills in the columns the schema requires of a submitted application.</summary>
    private async Task Applicant(Guid eventId, string email, string status)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications
                (event_id, email, status, first_name, last_name, age, phone, school,
                 level_of_study, country, mlh_coc_agreed_at, mlh_data_sharing_at,
                 submitted_at)
            VALUES (@eventId, @email, @status, 'Ada', 'Lovelace', 20, '+15550000000',
                    'Morgan State University', 'undergraduate', 'United States',
                    now(), now(), now())
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync();
    }

    // ------------------------------------------------------------------- rows ---

    /// <summary>The template row a campaign points at, whatever its key now means.</summary>
    private async Task<(string Subject, string Html)> TemplateBehind(Guid campaignId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT t.subject, t.body_html
              FROM notify.campaigns c
              JOIN notify.templates t ON t.id = c.template_id
             WHERE c.id = @id
            """);
        cmd.Parameters.AddWithValue("id", campaignId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetString(1));
    }

    private Task<int> RowsFor(string key) =>
        CountAsync("SELECT count(*) FROM notify.templates WHERE key = @key", key);

    private Task<int> LiveRowsFor(string key) =>
        CountAsync(
            "SELECT count(*) FROM notify.templates WHERE key = @key AND superseded_at IS NULL",
            key);

    private async Task<int> CountAsync(string sql, string key)
    {
        await using var cmd = db.DataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("key", key);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> TemplateCount()
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM notify.templates");
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> MessageCount(Guid campaignId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM notify.messages WHERE campaign_id = @id");
        cmd.Parameters.AddWithValue("id", campaignId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }
}
