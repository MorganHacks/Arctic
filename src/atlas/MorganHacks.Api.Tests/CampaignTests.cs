using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Broadcasts, against a real database running the real migrations.
/// </summary>
/// <remarks>
/// Half of what is being tested here is enforced by indexes and check
/// constraints rather than by C#, so a hand-rolled schema or an in-memory
/// double would pass whether or not the rules exist. The duplicate-send test
/// in particular is a test of a unique index and a conditional UPDATE, and it
/// is only worth anything against Postgres.
/// <para>
/// The template these send is inserted by the tests rather than by a
/// migration. Templates are data and their wording belongs to the people who
/// send the mail; what is here is deliberately nonsense, so that nothing in
/// this file can be mistaken for approved copy.
/// </para>
/// </remarks>
public class CampaignTests(ApplicationsDatabase db)
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

    // ------------------------------------------------------------- drafting ---

    [Fact]
    public async Task A_draft_mails_nobody()
    {
        // The split the whole surface is built on: writing a campaign down and
        // sending it are different actions behind different permissions, and
        // the first one must not put a single row in the queue.
        var (author, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, "drafted@example.com", "accepted");

        var created = await Create(cookie, InStatus(eventId, "accepted"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var campaign = await Body(created);
        Assert.Equal("draft", campaign.GetProperty("status").GetString());
        Assert.Equal(0, campaign.GetProperty("recipientCount").GetInt32());
        Assert.Equal(0, await MessageCount(Id(campaign)));

        // The author is recorded now, because the send is going to compare
        // against it.
        Assert.Equal(author, campaign.GetProperty("createdBy").GetGuid());
    }

    [Fact]
    public async Task The_stored_segment_is_the_one_the_server_understood()
    {
        // "Stored, not just executed." A month later somebody has to be able
        // to read what a campaign was aimed at, so what lands on the row is
        // the parsed segment rather than whatever JSON arrived — extra
        // properties and all.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();

        var response = await Client().SendAsync(Request(HttpMethod.Post, "/admin/campaigns", cookie,
            new
            {
                name = "Decisions",
                templateKey = await Template(),
                segment = new
                {
                    type = "applicationStatus",
                    eventId,
                    statuses = new[] { "accepted" },
                    somethingNobodyReads = "should not be stored",
                },
            }));

        var segment = (await Body(response)).GetProperty("segment");

        Assert.Equal("applicationStatus", segment.GetProperty("type").GetString());
        Assert.Equal(eventId, segment.GetProperty("eventId").GetGuid());
        Assert.False(segment.TryGetProperty("somethingNobodyReads", out _));
    }

    [Fact]
    public async Task A_transactional_template_cannot_be_broadcast()
    {
        // The lane rule at its earliest point. magic_link is priority 0 and
        // sends from the subdomain login mail depends on; a campaign pointed
        // at it would put several hundred announcements in front of every
        // sign-in link in the table.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();

        var response = await Client().SendAsync(Request(HttpMethod.Post, "/admin/campaigns", cookie,
            new
            {
                name = "Wrong lane",
                templateKey = "magic_link",
                segment = new { type = "applicationStatus", eventId, statuses = new[] { "accepted" } },
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------ two people ---

    [Fact]
    public async Task The_person_who_drafted_it_cannot_send_it()
    {
        // The approved_by column read the strong way. A duplicate or
        // misaddressed blast cannot be taken back, and the person least likely
        // to spot the mistake is the one who just made it.
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, "alone@example.com", "accepted");

        var id = Id(await Body(await Create(cookie, InStatus(eventId, "accepted"))));

        var response = await Send(id, cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await MessageCount(id));
    }

    [Fact]
    public async Task A_second_organizer_sends_it_and_is_recorded_as_the_approver()
    {
        var (author, drafting) = await Comms();
        var (approver, sending) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, "reader@example.com", "accepted");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        var response = await Send(id, sending);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await Body(response)).GetProperty("queued").GetInt32());

        var campaign = await Body(await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/campaigns/{id}", sending)));

        // Two names, and they are different names. This is the record that
        // makes the rule worth having.
        Assert.Equal(author, campaign.GetProperty("campaign").GetProperty("createdBy").GetGuid());
        Assert.Equal(approver, campaign.GetProperty("campaign").GetProperty("approvedBy").GetGuid());
    }

    // ----------------------------------------------------------- permissions ---

    [Fact]
    public async Task Drafting_needs_more_than_being_able_to_read_the_stats()
    {
        var reader = await db.AddPersonAsync(Unique("stats"));
        await db.GrantAsync(reader, "email.view_stats");
        var cookie = await SignIn(reader);
        var eventId = await db.AddEventAsync();

        Assert.Equal(HttpStatusCode.OK,
            (await Client().SendAsync(Request(HttpMethod.Get, "/admin/campaigns", cookie))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Create(cookie, InStatus(eventId, "accepted"))).StatusCode);
    }

    [Fact]
    public async Task Sending_needs_more_than_drafting()
    {
        // Registration's baseline holds email.send_templated and nothing
        // broader. Being able to write a campaign down is not being able to
        // make it leave the building.
        var (_, drafting) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, "unsendable@example.com", "accepted");
        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));

        var drafter = await db.AddPersonAsync(Unique("drafter"));
        await db.GrantAsync(drafter, "email.manage_templates");
        var cookie = await SignIn(drafter);

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(id, cookie)).StatusCode);
        Assert.Equal(0, await MessageCount(id));
    }

    [Fact]
    public async Task Reading_the_stats_does_not_reveal_who_was_mailed()
    {
        // email.view_stats answers "did that go out". Who received it is a
        // different question, and it belongs with drafting — which is where
        // the preview already shows addresses.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();
        var recipient = Unique("mailed");
        await Applicant(eventId, recipient, "accepted");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        await Send(id, sending);

        var reader = await db.AddPersonAsync(Unique("stats-only"));
        await db.GrantAsync(reader, "email.view_stats");
        var cookie = await SignIn(reader);

        var response = await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/campaigns/{id}", cookie));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(recipient, body, StringComparison.OrdinalIgnoreCase);

        // The counts are still there, because that is what the permission is
        // for.
        Assert.Equal(1, (await Body(response)).GetProperty("messages")
            .GetProperty("total").GetInt32());

        // And somebody who drafts campaigns does see them.
        var seen = await (await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/campaigns/{id}", drafting)))
            .Content.ReadAsStringAsync();

        Assert.Contains(recipient, seen, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_session_reaches_nothing_here()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client().GetAsync("/admin/campaigns")).StatusCode);
    }

    // ------------------------------------------------------------ the big one ---

    [Fact]
    public async Task Sending_twice_mails_everybody_once()
    {
        // The single worst failure available here. Several hundred people
        // receiving a duplicate is not recoverable, unlike almost everything
        // else in this system.
        //
        // Two guards are being tested at once and that is deliberate: the
        // conditional transition out of 'draft' is what makes the second
        // request answer 409, and the unique indexes on notify.messages are
        // what make the row count right even if it did not.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();

        foreach (var name in new[] { "one", "two", "three" })
        {
            await Applicant(eventId, $"{name}-{Guid.NewGuid():N}@example.com", "accepted");
        }

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));

        Assert.Equal(HttpStatusCode.OK, (await Send(id, sending)).StatusCode);
        Assert.Equal(3, await MessageCount(id));

        var again = await Send(id, sending);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(3, await MessageCount(id));
    }

    [Fact]
    public async Task Two_sends_at_the_same_instant_still_mail_everybody_once()
    {
        // The version a double-click actually produces. The guard is a
        // conditional UPDATE inside the transaction that writes the messages,
        // so the second request blocks on the campaign row and then finds a
        // status that is no longer 'draft' — rather than reading the status
        // first and racing between the read and the write.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();

        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            await Applicant(eventId, $"{name}-{Guid.NewGuid():N}@example.com", "accepted");
        }

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));

        var first = Send(id, sending);
        var second = Send(id, sending);
        var responses = await Task.WhenAll(first, second);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(4, await MessageCount(id));
    }

    [Fact]
    public async Task An_address_listed_twice_is_one_recipient()
    {
        // The gap the person-based unique index from 0003 does not cover: an
        // explicit list resolves with person_id NULL, and NULLs do not
        // conflict. 0015's (campaign_id, to_email) index is what holds here,
        // and the parser deduplicates before it so the preview number is
        // honest too.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();

        var address = Unique("mentor");
        var id = Id(await Body(await Create(drafting, new
        {
            type = "explicitList",
            emails = new[] { address, address.ToUpperInvariant(), Unique("other") },
        })));

        Assert.Equal(HttpStatusCode.OK, (await Send(id, sending)).StatusCode);
        Assert.Equal(2, await MessageCount(id));
    }

    // ---------------------------------------------------------- suppressions ---

    [Fact]
    public async Task A_hard_bounce_is_recorded_and_never_queued()
    {
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();

        var dead = Unique("bounced");
        var alive = Unique("fine");
        await Applicant(eventId, dead, "accepted");
        await Applicant(eventId, alive, "accepted");
        await Suppress(dead, "hard_bounce");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        var response = await Body(await Send(id, sending));

        Assert.Equal(1, response.GetProperty("queued").GetInt32());
        Assert.Equal(1, response.GetProperty("suppressed").GetInt32());

        // Written, not dropped. "Who were we about to email" has to keep an
        // answer, or a campaign that says 412 and sends 411 leaves nobody able
        // to find the one.
        Assert.Equal("suppressed", await StatusOf(id, dead));
        Assert.Equal("pending", await StatusOf(id, alive));
    }

    [Fact]
    public async Task An_unsubscribe_blocks_the_broadcast()
    {
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();

        var gone = Unique("unsubscribed");
        await Applicant(eventId, gone, "accepted");
        await Suppress(gone, "unsubscribed");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        var response = await Send(id, sending);

        // Nothing to send once the only recipient has opted out.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await Body(response)).GetProperty("queued").GetInt32());
        Assert.Equal("suppressed", await StatusOf(id, gone));
    }

    [Fact]
    public async Task An_unsubscribe_never_stands_between_somebody_and_a_sign_in_link()
    {
        // The rule the runbook calls out by name: if an unsubscribe stops a
        // sign-in link, that is a bug worth reporting rather than working
        // around. Asserted against the claim query itself, because that is
        // where a login link is actually handed to a sender.
        var address = Unique("opted-out");
        await Suppress(address, "unsubscribed");
        await DrainAsync();

        var queue = new MessageQueue(db.DataSource);
        var templates = new TemplateStore(db.DataSource);
        var magicLink = await templates.FindAsync("magic_link");
        Assert.NotNull(magicLink);

        await queue.EnqueueTransactionalAsync(
            magicLink, address, null,
            new Dictionary<string, string> { ["link"] = "https://example.invalid/x" });

        var claimed = await queue.ClaimAsync("test-worker", 10);

        Assert.Contains(claimed, m => m.ToEmail == address);
    }

    // ----------------------------------------------------------------- lanes ---

    [Fact]
    public async Task A_broadcast_never_queues_ahead_of_a_sign_in_link()
    {
        // Queued first, in bulk, and still behind. The ordering is lark's
        // (priority, created_at) claim and the priority is written as a
        // constant on the broadcast path, so nothing about this depends on a
        // template being configured correctly.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();

        await DrainAsync();

        foreach (var i in Enumerable.Range(0, 5))
        {
            await Applicant(eventId, $"crowd-{i}-{Guid.NewGuid():N}@example.com", "accepted");
        }

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        Assert.Equal(HttpStatusCode.OK, (await Send(id, sending)).StatusCode);

        Assert.Equal(5, await PendingAtPriority(10));

        var queue = new MessageQueue(db.DataSource);
        var templates = new TemplateStore(db.DataSource);
        var magicLink = (await templates.FindAsync("magic_link"))!;

        var signingIn = Unique("late");
        await queue.EnqueueTransactionalAsync(
            magicLink, signingIn, null,
            new Dictionary<string, string> { ["link"] = "https://example.invalid/x" });

        // One at a time, so this is genuinely about ordering rather than about
        // a batch happening to contain both.
        var claimed = await queue.ClaimAsync("test-worker", 1);

        Assert.Single(claimed);
        Assert.Equal(signingIn, claimed[0].ToEmail);
        Assert.Equal((short)0, claimed[0].Priority);
    }

    // ------------------------------------------------------------- freezing ---

    [Fact]
    public async Task The_recipient_list_is_frozen_at_send()
    {
        // Re-running a filter a month later gives a different answer, which is
        // exactly why the answer is written down. Somebody accepted after the
        // send is not somebody this campaign emailed.
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("early"), "accepted");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        await Send(id, sending);

        await Applicant(eventId, Unique("later"), "accepted");

        Assert.Equal(1, await MessageCount(id));

        var campaign = await Body(await Client().SendAsync(
            Request(HttpMethod.Get, $"/admin/campaigns/{id}", sending)));

        Assert.Equal(1, campaign.GetProperty("campaign").GetProperty("recipientCount").GetInt32());
    }

    [Fact]
    public async Task Preview_counts_the_segment_without_freezing_it()
    {
        var (_, cookie) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("previewed"), "accepted");

        var id = Id(await Body(await Create(cookie, InStatus(eventId, "accepted"))));

        var first = await Body(await Client().SendAsync(
            Request(HttpMethod.Post, $"/admin/campaigns/{id}/preview", cookie)));

        Assert.Equal(1, first.GetProperty("recipientCount").GetInt32());
        Assert.Single(first.GetProperty("sample").EnumerateArray());
        Assert.Equal(0, await MessageCount(id));

        await Applicant(eventId, Unique("appeared"), "accepted");

        var second = await Body(await Client().SendAsync(
            Request(HttpMethod.Post, $"/admin/campaigns/{id}/preview", cookie)));

        // Two answers from one segment, which is the whole reason the send
        // freezes its own.
        Assert.Equal(2, second.GetProperty("recipientCount").GetInt32());
    }

    // ------------------------------------------------------------ cancelling ---

    [Fact]
    public async Task Cancelling_stops_what_has_not_gone_and_says_what_had()
    {
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();

        foreach (var i in Enumerable.Range(0, 3))
        {
            await Applicant(eventId, $"stop-{i}-{Guid.NewGuid():N}@example.com", "accepted");
        }

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));
        await Send(id, sending);

        // One of them is already at the provider, which cancelling cannot take
        // back and must not claim to have.
        await MarkOneSent(id);

        var response = await Client().SendAsync(
            Request(HttpMethod.Post, $"/admin/campaigns/{id}/cancel", sending));
        var body = await Body(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, body.GetProperty("stopped").GetInt32());
        Assert.Equal(1, body.GetProperty("alreadySent").GetInt32());
        Assert.Equal(0, await PendingIn(id));
    }

    [Fact]
    public async Task A_cancelled_campaign_cannot_then_be_sent()
    {
        var (_, drafting) = await Comms();
        var (_, sending) = await Comms();
        var eventId = await db.AddEventAsync();
        await Applicant(eventId, Unique("abandoned"), "accepted");

        var id = Id(await Body(await Create(drafting, InStatus(eventId, "accepted"))));

        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(
            Request(HttpMethod.Post, $"/admin/campaigns/{id}/cancel", sending))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await Send(id, sending)).StatusCode);
        Assert.Equal(0, await MessageCount(id));
    }

    // ----------------------------------------------------------------- setup ---

    private HttpClient Client() => _app.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>An organizer on comms, which is the team that sends broadcasts.</summary>
    /// <remarks>
    /// Through the seeded baseline rather than a hand-written grant, so these
    /// tests fail if the migration that puts <c>email.send_broadcast</c> on
    /// comms is ever changed — which is the point of testing against the real
    /// schema.
    /// </remarks>
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

    private static object InStatus(Guid eventId, params string[] statuses) =>
        new { type = "applicationStatus", eventId, statuses };

    private async Task<HttpResponseMessage> Create(string cookie, object segment) =>
        await Client().SendAsync(Request(HttpMethod.Post, "/admin/campaigns", cookie, new
        {
            name = "A campaign",
            templateKey = await Template(),
            segment,
        }));

    private Task<HttpResponseMessage> Send(Guid id, string cookie) =>
        Client().SendAsync(Request(HttpMethod.Post, $"/admin/campaigns/{id}/send", cookie));

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

    private static Guid Id(JsonElement campaign) => campaign.GetProperty("id").GetGuid();

    // ------------------------------------------------------------- fixtures ---

    private string? _templateKey;

    /// <summary>
    /// A broadcast template with deliberately meaningless copy.
    /// </summary>
    /// <remarks>
    /// Inserted here rather than seeded by a migration. Template wording is
    /// approved by the people who send the mail, and a plausible-looking body
    /// committed to a migration is a body somebody will eventually send.
    /// </remarks>
    private async Task<string> Template()
    {
        if (_templateKey is not null)
        {
            return _templateKey;
        }

        var key = $"test-broadcast-{Guid.NewGuid():N}";
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO notify.templates
                (key, kind, subject, body_html, body_text, from_local, from_domain)
            VALUES (@key, 'broadcast', 'placeholder subject',
                    '<p>placeholder</p>', 'placeholder', 'news', 'news.example.invalid')
            """);
        cmd.Parameters.AddWithValue("key", key);
        await cmd.ExecuteNonQueryAsync();

        return _templateKey = key;
    }

    /// <summary>
    /// An application complete enough for the schema to accept a real status.
    /// </summary>
    /// <remarks>
    /// Every column here is one the <c>submitted_applications_are_complete</c>
    /// check requires. Filling them is not ceremony — a segment resolved
    /// against half-written rows would be testing a shape the database does
    /// not allow to exist.
    /// </remarks>
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

    private async Task Suppress(string email, string reason)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO notify.suppressions (email, reason) VALUES (@email, @reason)
            ON CONFLICT (email) DO UPDATE SET reason = EXCLUDED.reason
            """);
        cmd.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = email });
        cmd.Parameters.AddWithValue("reason", reason);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Empties the queue of anything an earlier test left pending.
    /// </summary>
    /// <remarks>
    /// The ordering tests claim from the same table every other test in this
    /// class has been filling, and a leftover row from three tests ago would
    /// make one of them pass or fail for a reason that has nothing to do with
    /// priority.
    /// </remarks>
    private async Task DrainAsync()
    {
        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE notify.messages SET status = 'cancelled' WHERE status = 'pending'");
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> MessageCount(Guid campaignId) =>
        await CountAsync("SELECT count(*) FROM notify.messages WHERE campaign_id = @id", campaignId);

    private async Task<int> PendingIn(Guid campaignId) =>
        await CountAsync(
            "SELECT count(*) FROM notify.messages WHERE campaign_id = @id AND status = 'pending'",
            campaignId);

    private async Task<int> PendingAtPriority(short priority)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM notify.messages WHERE status = 'pending' AND priority = @p");
        cmd.Parameters.AddWithValue("p", priority);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> CountAsync(string sql, Guid campaignId)
    {
        await using var cmd = db.DataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", campaignId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string?> StatusOf(Guid campaignId, string email)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT status FROM notify.messages WHERE campaign_id = @id AND to_email = @email");
        cmd.Parameters.AddWithValue("id", campaignId);
        cmd.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = email });
        return await cmd.ExecuteScalarAsync() as string;
    }

    /// <summary>Stands in for a worker having already handed one to SES.</summary>
    private async Task MarkOneSent(Guid campaignId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            UPDATE notify.messages SET status = 'sent', sent_at = now()
             WHERE id = (SELECT id FROM notify.messages
                          WHERE campaign_id = @id AND status = 'pending' LIMIT 1)
            """);
        cmd.Parameters.AddWithValue("id", campaignId);
        await cmd.ExecuteNonQueryAsync();
    }
}
