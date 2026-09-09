using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Live announcements: posting one, taking it back down, and who reads it.
/// </summary>
/// <remarks>
/// Against a real database and the real seeded baselines, because most of what
/// matters here is in neither the endpoint nor the store. Who may post is a
/// row <c>0024</c> writes rather than a grant a test can invent, the length
/// limit is also a check constraint, and the rule that an applicant only ever
/// sees their own event's notices is a subquery — none of which a mocked store
/// would be exercising.
/// <para>
/// Nothing in this file uses text that could be mistaken for a real notice.
/// The bodies are numbered placeholders on purpose: a fixture that read
/// "judging has moved to 2pm" is one somebody eventually screenshots.
/// </para>
/// </remarks>
public class AnnouncementTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);

            // Said here rather than inherited from features.json, like
            // PortalTests. The portal ships off by default and a suite that
            // read the default would go red every time somebody moved a
            // switch, which is the opposite of what a switch is for.
            b.UseSetting("enable_hacker_portal_feature", "true");
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------ the gate ---

    [Fact]
    public async Task Every_route_refuses_a_caller_with_no_session()
    {
        // On every route rather than the one somebody remembered. Three of
        // these put words in front of every hacker at an event and the fourth
        // is somebody's own portal.
        var id = Guid.NewGuid();
        (HttpMethod Method, string Path)[] routes =
        [
            (HttpMethod.Get, $"/admin/events/{id}/announcements"),
            (HttpMethod.Post, $"/admin/events/{id}/announcements"),
            (HttpMethod.Post, $"/admin/announcements/{id}/retract"),
            (HttpMethod.Get, "/portal/announcements"),
        ];

        foreach (var (method, path) in routes)
        {
            var response = await _app.CreateClient()
                .SendAsync(new HttpRequestMessage(method, path));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Reading_applications_is_not_enough_to_post_a_notice()
    {
        // The split announcements.post exists for. applications.view is held
        // by every team that works the queue; putting a sentence in front of
        // every hacker in the building is a smaller group than that.
        var reader = await OrganizerAsync(Permission.ApplicationsView.Value);
        var eventId = await db.AddEventAsync();
        var posted = await PostAsync(eventId, "Announcement one");

        var listed = await Send(
            HttpMethod.Get, $"/admin/events/{eventId}/announcements", reader);
        var created = await Send(
            HttpMethod.Post, $"/admin/events/{eventId}/announcements", reader,
            new { body = "Announcement two" });
        var retracted = await Send(
            HttpMethod.Post, $"/admin/announcements/{posted}/retract", reader);

        Assert.Equal(HttpStatusCode.Forbidden, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, retracted.StatusCode);
    }

    [Fact]
    public async Task Logistics_may_post_a_notice_and_a_judge_may_not()
    {
        // The baselines the migration writes, not grants made up by this test.
        // Logistics is on the list because they are the team standing in the
        // room when the schedule moves; a judge holds judging.score_assigned
        // and nothing else, and must not be able to address the whole floor.
        var logistics = await TeamMemberAsync("logistics");
        var judge = await TeamMemberAsync("judge");
        var eventId = await db.AddEventAsync();

        var allowed = await Send(
            HttpMethod.Post, $"/admin/events/{eventId}/announcements", logistics,
            new { body = "Announcement one" });
        var refused = await Send(
            HttpMethod.Post, $"/admin/events/{eventId}/announcements", judge,
            new { body = "Announcement two" });

        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Comms_and_super_admin_hold_it_too()
    {
        // The other two teams 0024 names. Comms because they already hold
        // email.send_broadcast, which is the same act one step heavier.
        var eventId = await db.AddEventAsync();

        foreach (var team in new[] { "comms", "super-admin" })
        {
            var response = await Send(
                HttpMethod.Post, $"/admin/events/{eventId}/announcements",
                await TeamMemberAsync(team), new { body = $"Announcement from {team}" });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    // --------------------------------------------------------- what is read ---

    [Fact]
    public async Task An_applicant_reads_what_was_posted_to_their_event()
    {
        // The whole point of the feature: an organizer types one line and
        // somebody signed in to the portal can read it, with no email sent.
        var eventId = await db.AddEventAsync();
        var person = await ApplicantAsync(eventId);
        await PostAsync(eventId, "Announcement one");

        var feed = await FeedAsync(person);

        Assert.Single(feed);
        Assert.Equal("Announcement one", feed[0]!["body"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_retracted_notice_leaves_the_feed_and_stays_on_the_list()
    {
        // Both halves of the same decision. The applicant stops seeing it,
        // because a notice that has been taken back is not information; the
        // organizer keeps seeing it marked, because a list that quietly
        // dropped it is indistinguishable from one where the retraction never
        // landed, and the next thing that happens is somebody retracting it
        // again to be sure.
        var eventId = await db.AddEventAsync();
        var person = await ApplicantAsync(eventId);

        var kept = await PostAsync(eventId, "Announcement one");
        var pulled = await PostAsync(eventId, "Announcement two");

        var retraction = await Send(
            HttpMethod.Post, $"/admin/announcements/{pulled}/retract", await ManagerAsync());
        Assert.Equal(HttpStatusCode.OK, retraction.StatusCode);

        var feed = await FeedAsync(person);
        var bodies = feed.Select(a => a!["body"]!.GetValue<string>()).ToArray();

        Assert.Equal(["Announcement one"], bodies);

        var listed = await ListAsync(eventId);
        Assert.Equal(2, listed.Count);
        Assert.True(Find(listed, pulled)!["retracted"]!.GetValue<bool>());
        Assert.False(Find(listed, kept)!["retracted"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_feed_is_newest_first()
    {
        // Newest first because the screen is read from the top by somebody
        // who last looked an hour ago. Oldest-first would put the correction
        // below the thing it corrects.
        var eventId = await db.AddEventAsync();
        var person = await ApplicantAsync(eventId);

        await PostAsync(eventId, "Announcement one");
        await PostAsync(eventId, "Announcement two");
        await PostAsync(eventId, "Announcement three");

        var feed = await FeedAsync(person);
        var bodies = feed.Select(a => a!["body"]!.GetValue<string>()).ToArray();

        Assert.Equal(
            ["Announcement three", "Announcement two", "Announcement one"], bodies);
    }

    [Fact]
    public async Task Another_events_notices_are_not_in_your_feed()
    {
        // The access rule, and the reason the store resolves the event from
        // the session rather than taking one from the request. Last year's
        // applicant must not read this year's floor notices, and there is no
        // url to edit that would let them.
        var mine = await db.AddEventAsync();
        var theirs = await db.AddEventAsync();
        var person = await ApplicantAsync(mine);

        await PostAsync(theirs, "Announcement for the other event");

        var feed = await FeedAsync(person);

        Assert.Empty(feed);
    }

    [Fact]
    public async Task Somebody_with_no_application_sees_no_notices()
    {
        // Signed in is not the same as being at the event. An organizer's own
        // session, or somebody who made an account and never applied, resolves
        // to no event and therefore to nothing — 200 with an empty list rather
        // than 404, because they are signed in and this is their portal.
        var eventId = await db.AddEventAsync();
        await PostAsync(eventId, "Announcement one");

        var stranger = await db.AddPersonAsync(Unique("stranger"));

        var feed = await FeedAsync(stranger);

        Assert.Empty(feed);
    }

    [Fact]
    public async Task The_feed_never_names_the_organizer_who_posted()
    {
        // A notice is the team speaking. Telling an applicant which human
        // typed it hands them somebody to be annoyed at about a schedule
        // change, and the retraction fields would be telling them about a
        // decision they were deliberately not shown.
        var eventId = await db.AddEventAsync();
        var person = await ApplicantAsync(eventId);
        var (cookie, organizer) = await ManagerWithIdAsync();

        await PostAsync(eventId, "Announcement one", cookie);

        var response = await Send(HttpMethod.Get, "/portal/announcements", await SignInAsync(person));
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(organizer.ToString(), body);
        Assert.DoesNotContain("postedBy", body);
        Assert.DoesNotContain("retracted", body);
    }

    // -------------------------------------------------------- what is written ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_notice_with_nothing_in_it_is_refused(string body)
    {
        // Whitespace as well as empty, because a body of spaces would be
        // stored and then rendered as a blank row an applicant has to scroll
        // past wondering what it said.
        var eventId = await db.AddEventAsync();

        var response = await Send(
            HttpMethod.Post, $"/admin/events/{eventId}/announcements",
            await ManagerAsync(), new { body });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_notice_longer_than_a_notice_is_refused()
    {
        // 500 is the column's limit as well as the endpoint's, so this proves
        // the two agree. If the endpoint's ever grew past the constraint's the
        // failure would be a 500 on the write rather than a sentence.
        var eventId = await db.AddEventAsync();

        var response = await Send(
            HttpMethod.Post, $"/admin/events/{eventId}/announcements",
            await ManagerAsync(), new { body = new string('a', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_database_refuses_a_notice_the_endpoint_would_have()
    {
        // The constraint is in 0024 rather than only in C# because this is
        // exactly the write a hand-run INSERT during the event skips.
        var eventId = await db.AddEventAsync();
        var organizer = await db.AddPersonAsync(Unique("hand"));

        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.announcements (event_id, body, posted_by)
            VALUES (@eventId, '   ', @postedBy)
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("postedBy", organizer);

        var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => cmd.ExecuteNonQueryAsync());

        Assert.Equal("23514", refused.SqlState);
    }

    [Fact]
    public async Task Posting_to_an_event_that_does_not_exist_says_so()
    {
        // A mistyped id, which is what this actually is, should read as "no
        // such event" rather than as the notice having gone somewhere.
        var response = await Send(
            HttpMethod.Post, $"/admin/events/{Guid.NewGuid()}/announcements",
            await ManagerAsync(), new { body = "Announcement one" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Retracting_a_notice_that_is_not_there_says_so()
    {
        var response = await Send(
            HttpMethod.Post, $"/admin/announcements/{Guid.NewGuid()}/retract",
            await ManagerAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_notice_records_who_posted_it_and_who_took_it_down()
    {
        // The columns are how "who said that" has an answer at all: there is
        // no audit trigger on this table, because audit.entries is for changes
        // to who may do what, and this is the convention the rest of the
        // content tables follow.
        var eventId = await db.AddEventAsync();
        var (author, authorId) = await ManagerWithIdAsync();
        var (censor, censorId) = await ManagerWithIdAsync();

        var id = await PostAsync(eventId, "Announcement one", author);
        var retraction = await Send(
            HttpMethod.Post, $"/admin/announcements/{id}/retract", censor);

        var body = await ReadAsync(retraction);

        Assert.Equal(authorId, body["postedBy"]!.GetValue<Guid>());
        Assert.Equal(censorId, body["retractedBy"]!.GetValue<Guid>());
        Assert.NotNull(body["retractedAt"]);
    }

    [Fact]
    public async Task A_second_retraction_changes_nothing_and_still_succeeds()
    {
        // Two organizers reaching for the same mistake is the normal case in
        // the ten minutes this gets used, and neither of them should be shown
        // an error for agreeing. The first retractor stays named on the row,
        // because who got there first is the fact worth keeping.
        var eventId = await db.AddEventAsync();
        var (first, firstId) = await ManagerWithIdAsync();
        var second = await ManagerAsync();

        var id = await PostAsync(eventId, "Announcement one");

        var once = await Send(HttpMethod.Post, $"/admin/announcements/{id}/retract", first);
        var twice = await Send(HttpMethod.Post, $"/admin/announcements/{id}/retract", second);

        Assert.Equal(HttpStatusCode.OK, once.StatusCode);
        Assert.Equal(HttpStatusCode.OK, twice.StatusCode);

        var body = await ReadAsync(twice);
        Assert.Equal(firstId, body["retractedBy"]!.GetValue<Guid>());
        Assert.Equal(
            (await ReadAsync(once))["retractedAt"]!.GetValue<DateTimeOffset>(),
            body["retractedAt"]!.GetValue<DateTimeOffset>());
    }

    // --------------------------------------------------------------- helpers ---

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    /// <summary>Posts a notice through the real endpoint and returns its id.</summary>
    private async Task<Guid> PostAsync(Guid eventId, string body, string? cookie = null)
    {
        cookie ??= await ManagerAsync();

        var response = await Send(
            HttpMethod.Post, $"/admin/events/{eventId}/announcements", cookie, new { body });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await ReadAsync(response))["id"]!.GetValue<Guid>();
    }

    /// <summary>The organizers' list for an event.</summary>
    private async Task<JsonArray> ListAsync(Guid eventId, string? cookie = null)
    {
        cookie ??= await ManagerAsync();

        var response = await Send(
            HttpMethod.Get, $"/admin/events/{eventId}/announcements", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await ReadAsync(response))["announcements"]!.AsArray();
    }

    /// <summary>The portal feed as one applicant sees it.</summary>
    private async Task<JsonArray> FeedAsync(Guid personId)
    {
        var response = await Send(
            HttpMethod.Get, "/portal/announcements", await SignInAsync(personId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await ReadAsync(response))["announcements"]!.AsArray();
    }

    private static JsonNode? Find(JsonArray announcements, Guid id) =>
        announcements.First(a => a!["id"]!.GetValue<Guid>() == id);

    /// <summary>An applicant with an application against one event.</summary>
    /// <remarks>
    /// A real row rather than a person on their own, because the feed is
    /// scoped by the event of the reader's application and somebody without
    /// one is a different test.
    /// </remarks>
    private async Task<Guid> ApplicantAsync(Guid eventId)
    {
        var person = await db.AddPersonAsync(Unique("applicant"));

        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications (event_id, person_id, email)
            VALUES (@eventId, @personId, @email)
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("personId", person);
        cmd.Parameters.AddWithValue("email", Unique("app"));
        await cmd.ExecuteNonQueryAsync();

        return person;
    }

    /// <summary>Somebody who may post, by a direct grant.</summary>
    private async Task<string> ManagerAsync() => (await ManagerWithIdAsync()).Cookie;

    private async Task<(string Cookie, Guid Person)> ManagerWithIdAsync()
    {
        var id = await db.AddPersonAsync(Unique("poster"));
        await db.GrantAsync(id, Permission.AnnouncementsPost.Value);
        return (await SignInAsync(id), id);
    }

    /// <summary>An organizer holding exactly the permissions named.</summary>
    private async Task<string> OrganizerAsync(params string[] permissions)
    {
        var id = await db.AddPersonAsync(Unique("organizer"));
        foreach (var permission in permissions)
        {
            await db.GrantAsync(id, permission);
        }

        return await SignInAsync(id);
    }

    /// <summary>Somebody whose only access is a seeded team baseline.</summary>
    private async Task<string> TeamMemberAsync(string slug)
    {
        var id = await db.AddPersonAsync(Unique("team"));
        await db.AddToTeamAsync(id, slug);
        return await SignInAsync(id);
    }

    private async Task<string> SignInAsync(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private Task<HttpResponseMessage> Send(
        HttpMethod method, string path, string cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        }).SendAsync(request);
    }
}
