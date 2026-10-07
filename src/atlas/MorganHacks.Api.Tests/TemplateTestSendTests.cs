using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api.Tests;

public class TemplateTestSendTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("html", "<p>Hello {{email}} and {{firstName}}</p><script>alert(1)</script>")]
    [InlineData("markdown", "Hello **{{email}}** and {{firstName}}")]
    public async Task Tests_queue_the_unsaved_design_once_without_publishing_a_template(string format, string body)
    {
        var (editor, own) = await Editor();
        using var _ = editor;
        var requestId = Guid.NewGuid();
        var key = $"unpublished-{Guid.NewGuid():N}";
        var request = new { requestId, recipient = own, draft = Draft(key, body, format) };
        var responses = await Task.WhenAll(editor.PostAsJsonAsync("/admin/templates/test", request), editor.PostAsJsonAsync("/admin/templates/test", request));
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(requestId, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync($"/admin/templates/{key}")).StatusCode);
        await using var read = db.DataSource.CreateCommand("""
            SELECT m.rendered_subject, m.rendered_body_html, m.rendered_body_text, m.to_email,
                   t.superseded_at, t.click_tracking, c.recipient_count, m.priority
            FROM notify.messages m JOIN notify.campaigns c ON c.id = m.campaign_id
            JOIN notify.templates t ON t.id = c.template_id WHERE m.id = @id
            """);
        read.Parameters.AddWithValue("id", requestId);
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("[Test] Placeholder subject", reader.GetString(0));
        Assert.Contains($"Preview {own}</div>", reader.GetString(1));
        Assert.Contains("{{firstName}}", reader.GetString(1));
        Assert.DoesNotContain("<script", reader.GetString(1));
        Assert.Contains(own, reader.GetString(2));
        Assert.Equal(own, reader.GetString(3));
        Assert.False(reader.IsDBNull(4));
        Assert.False(reader.GetBoolean(5));
        Assert.Equal(1, reader.GetInt32(6));
        Assert.Equal(10, reader.GetInt16(7));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Test_sending_needs_both_template_and_sending_permissions()
    {
        var (writer, writerOwn) = await Editor(send: false);
        using var _ = writer;
        var (sender, senderOwn) = await Editor(manage: false);
        using var __ = sender;
        using var anonymous = _app.CreateClient();
        var forbidden = Draft("test", "Body");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/admin/templates/test", new { requestId = Guid.NewGuid(), recipient = writerOwn, draft = forbidden })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await writer.PostAsJsonAsync("/admin/templates/test", new { requestId = Guid.NewGuid(), recipient = writerOwn, draft = forbidden })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sender.PostAsJsonAsync("/admin/templates/test", new { requestId = Guid.NewGuid(), recipient = senderOwn, draft = forbidden })).StatusCode);
    }

    [Theory]
    [InlineData("invalid", "Body")]
    [InlineData("test@example.invalid", "")]
    [InlineData("test@example.invalid,second@example.invalid", "Body")]
    [InlineData("test@example.invalid\r\nBcc:other@example.invalid", "Body")]
    public async Task Invalid_recipient_or_empty_body_cannot_queue_a_test(string recipient, string body)
    {
        var (editor, _) = await Editor();
        using var client = editor;
        var response = await editor.PostAsJsonAsync("/admin/templates/test", new { requestId = Guid.NewGuid(), recipient, draft = Draft("test", body) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Suppressed_addresses_cannot_receive_a_test()
    {
        var (editor, own) = await Editor();
        using var _ = editor;
        await new MessageQueue(db.DataSource).SuppressAsync(own, "hard_bounce");
        var response = await editor.PostAsJsonAsync("/admin/templates/test", new { requestId = Guid.NewGuid(), recipient = own, draft = Draft("test", "Body") });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A test send may only go to the organizer who asked for it.
    /// </summary>
    /// <remarks>
    /// The control that matters on this endpoint. Before it, any address was
    /// accepted, so a compromised organizer account could send arbitrary HTML
    /// from our sending identity to arbitrary recipients as fast as SES took
    /// it — and the sign-in links people need share that reputation. A rate
    /// limit would not have closed it; refusing the other recipients does.
    /// </remarks>
    [Fact]
    public async Task A_test_cannot_be_addressed_to_anybody_but_the_organizer_asking_for_it()
    {
        var (editor, own) = await Editor();
        using var _ = editor;
        var response = await editor.PostAsJsonAsync("/admin/templates/test",
            new { requestId = Guid.NewGuid(), recipient = $"somebody-else-{Guid.NewGuid():N}@example.invalid", draft = Draft("test", "Body") });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Somebody else's address is refused whether or not the actor may use it.
    /// </summary>
    /// <remarks>
    /// The obvious way to dodge the self-send rule is to send as yourself to
    /// somebody else and then, separately, confirm that address is a person
    /// here. Both are needed to make the rule hold, so this checks the
    /// recipient column of <c>notify.messages</c> rather than trusting a 400.
    /// </remarks>
    [Fact]
    public async Task A_refused_recipient_leaves_nothing_queued()
    {
        var (editor, _) = await Editor();
        using var _ = editor;
        var strays = $"stray-{Guid.NewGuid():N}@example.invalid";
        var response = await editor.PostAsJsonAsync("/admin/templates/test",
            new { requestId = Guid.NewGuid(), recipient = strays, draft = Draft("test", "Body") });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var read = db.DataSource.CreateCommand("SELECT count(*) FROM notify.messages WHERE to_email = @e");
        read.Parameters.AddWithValue("e", strays);
        Assert.Equal(0L, (long)(await read.ExecuteScalarAsync())!);
    }

    /// <summary>
    /// Casing in the dialog is not somebody else's address.
    /// </summary>
    /// <remarks>
    /// The stored address and the typed one are the same address written by two
    /// people, and a test refused over the casing of a local part is a bug
    /// report rather than a control.
    /// </remarks>
    [Fact]
    public async Task An_organizers_own_address_is_accepted_however_it_is_cased()
    {
        var (editor, own) = await Editor();
        using var _ = editor;
        var shouted = own.ToUpperInvariant();
        var response = await editor.PostAsJsonAsync("/admin/templates/test",
            new { requestId = Guid.NewGuid(), recipient = shouted, draft = Draft("test", "Body") });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    /// <summary>
    /// One organizer cannot spend another organizer's allowance.
    /// </summary>
    /// <remarks>
    /// The hourly limit is keyed on the person, not on the address they are
    /// sending from — every per-address limiter in this API is loose by
    /// necessity because a campus NAT is a whole building, but the caller here
    /// is authenticated and the count is theirs alone. Exhausted, the next
    /// organizer is still served.
    /// </remarks>
    [Fact]
    public async Task Test_sends_are_capped_per_organizer()
    {
        var (noisy, own) = await Editor();
        using var _ = noisy;
        for (var i = 0; i < 30; i++)
        {
            var ok = await noisy.PostAsJsonAsync("/admin/templates/test",
                new { requestId = Guid.NewGuid(), recipient = own, draft = Draft("test", "Body") });
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await noisy.PostAsJsonAsync("/admin/templates/test",
            new { requestId = Guid.NewGuid(), recipient = own, draft = Draft("test", "Body") })).StatusCode);

        var (quiet, quietOwn) = await Editor();
        using var __ = quiet;
        Assert.Equal(HttpStatusCode.Accepted, (await quiet.PostAsJsonAsync("/admin/templates/test",
            new { requestId = Guid.NewGuid(), recipient = quietOwn, draft = Draft("test", "Body") })).StatusCode);
    }

    private static object Draft(string key, string body, string format = "markdown") => new
    {
        key,
        name = "Test fixture",
        kind = "broadcast",
        subject = "Placeholder subject",
        body,
        format,
        fromLocal = "mail",
        fromDomain = "example.invalid",
        fromName = "Sender",
        previewText = "Preview {{email}}",
        clickTracking = true,
    };

    /// <summary>An editor session, and the address a test send to them may use.</summary>
    private async Task<(HttpClient Client, string Own)> Editor(bool manage = true, bool send = true)
    {
        var own = $"editor-{Guid.NewGuid():N}@example.invalid";
        var id = await db.AddPersonAsync(own);
        if (manage) await db.GrantAsync(id, "email.manage_templates");
        if (send) await db.GrantAsync(id, "email.send_templated");
        using var scope = _app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(id);
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");
        return (client, own);
    }
}