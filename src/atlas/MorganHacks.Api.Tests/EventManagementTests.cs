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
/// Making an event, and setting its dates afterwards.
/// </summary>
/// <remarks>
/// Against a real database and the real seeded baselines, because half of what
/// is being tested here lives in neither the endpoint nor the store: the slug
/// rule is also a check constraint, and who may create an event is a row the
/// migration writes rather than a grant a test can invent.
/// </remarks>
public class EventManagementTests(ApplicationsDatabase db)
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

    // ------------------------------------------------------------ the gate ---

    [Fact]
    public async Task Every_route_refuses_a_caller_with_no_session()
    {
        // On every route rather than the one somebody remembered. This is the
        // surface that makes the root object everything else hangs off.
        var id = Guid.NewGuid();
        (HttpMethod Method, string Path)[] routes =
        [
            (HttpMethod.Get, "/admin/events"),
            (HttpMethod.Post, "/admin/events"),
            (HttpMethod.Put, $"/admin/events/{id}"),
        ];

        foreach (var (method, path) in routes)
        {
            var response = await _app.CreateClient()
                .SendAsync(new HttpRequestMessage(method, path));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Reading_the_events_is_not_enough_to_make_one()
    {
        // The split the permission exists for. Every team that works the queue
        // holds applications.view and already sees this list inside the forms
        // and applicants screens; creating the season is a smaller group than
        // that.
        var reader = await OrganizerAsync(Permission.ApplicationsView.Value);
        var existing = await MakeEventAsync();

        var listed = await Send(HttpMethod.Get, "/admin/events", reader);
        var created = await Send(HttpMethod.Post, "/admin/events", reader,
            new { slug = Slug(), name = "Refused" });
        var updated = await Send(HttpMethod.Put, $"/admin/events/{existing}", reader,
            new { name = "Refused" });

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, updated.StatusCode);
    }

    [Fact]
    public async Task Super_admin_makes_events_and_registration_does_not()
    {
        // The baseline the migration writes, not a grant made up by this test.
        // Granting events.manage by hand here would pass whether or not 0020
        // ever put it on the super-admin team.
        var admin = await TeamMemberAsync("super-admin");
        var registration = await TeamMemberAsync("registration");

        var allowed = await Send(HttpMethod.Post, "/admin/events", admin,
            new { slug = Slug(), name = "Test event" });
        var refused = await Send(HttpMethod.Post, "/admin/events", registration,
            new { slug = Slug(), name = "Test event" });

        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    // ---------------------------------------------------------- making one ---

    [Fact]
    public async Task An_event_is_made_from_a_slug_and_a_name_and_nothing_else()
    {
        // The whole point of the shape. None of the dates is agreed on the day
        // somebody decides next season is happening, and demanding one here
        // would mean inventing it.
        var cookie = await ManagerAsync();
        var slug = Slug();

        var response = await Send(HttpMethod.Post, "/admin/events", cookie,
            new { slug, name = "Test event" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadAsync(response);
        Assert.Equal(slug, body["slug"]!.GetValue<string>());
        Assert.Equal("Test event", body["name"]!.GetValue<string>());

        foreach (var field in new[]
        {
            "startsAt", "endsAt", "registrationOpensAt",
            "registrationClosesAt", "decisionsAnnouncedAt", "capacity",
        })
        {
            Assert.Null(body[field]);
        }
    }

    [Fact]
    public async Task A_new_event_records_who_made_it()
    {
        // The column exists because "who created this" used to have no answer
        // at all: every event so far was a hand-written INSERT.
        var (cookie, person) = await ManagerWithIdAsync();

        var response = await Send(HttpMethod.Post, "/admin/events", cookie,
            new { slug = Slug(), name = "Test event" });
        response.EnsureSuccessStatusCode();

        Assert.Equal(person, (await ReadAsync(response))["createdBy"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task A_new_event_shows_up_in_the_list()
    {
        // applications.view as well, because that is what reading the list
        // takes. The same gate the form builder puts on its own reads, and for
        // the same reason: this list is already inside the forms and
        // applicants responses that group loads.
        var cookie = await ManagerAsync(Permission.ApplicationsView.Value);
        var slug = Slug();

        await (await Send(HttpMethod.Post, "/admin/events", cookie,
            new { slug, name = "Test event" })).EnsureSuccess();

        var listed = await Send(HttpMethod.Get, "/admin/events", cookie);
        listed.EnsureSuccessStatusCode();

        Assert.Contains(
            (await ReadAsync(listed))["events"]!.AsArray(),
            e => e!["slug"]!.GetValue<string>() == slug);
    }

    [Fact]
    public async Task An_event_needs_a_name_and_a_slug()
    {
        var cookie = await ManagerAsync();

        var nameless = await Send(HttpMethod.Post, "/admin/events", cookie,
            new { slug = Slug(), name = "   " });
        var slugless = await Send(HttpMethod.Post, "/admin/events", cookie,
            new { name = "Test event" });

        Assert.Equal(HttpStatusCode.BadRequest, nameless.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, slugless.StatusCode);
    }

    // --------------------------------------------------------- the slug ---

    [Fact]
    public async Task A_slug_is_normalised_rather_than_taken_as_typed()
    {
        // A trailing space and a capital letter are typing, and there is
        // exactly one thing either could have meant. Left as typed they become
        // two rows that are one link to a person.
        var cookie = await ManagerAsync();
        var slug = Slug();

        var response = await Send(HttpMethod.Post, "/admin/events", cookie,
            new { slug = $"  {slug.ToUpperInvariant()}  ", name = "Test event" });
        response.EnsureSuccessStatusCode();

        Assert.Equal(slug, (await ReadAsync(response))["slug"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("season/2")]      // a slash is another path segment wherever this lands
    [InlineData("season 2")]      // a space arrives percent-encoded and unreadable
    [InlineData("season_2")]
    [InlineData("season.2")]
    [InlineData("-season")]
    [InlineData("season-")]
    [InlineData("season--two")]
    [InlineData("s")]
    [InlineData("héllo")]
    public async Task A_slug_that_would_break_a_link_is_refused(string slug)
    {
        var cookie = await ManagerAsync();

        var response = await Send(HttpMethod.Post, "/admin/events", cookie,
            new { slug, name = "Test event" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_slug_belongs_to_one_event()
    {
        // 409 rather than 400: the request is well formed and the identifier
        // is simply spoken for.
        var cookie = await ManagerAsync();
        var slug = Slug();

        await (await Send(HttpMethod.Post, "/admin/events", cookie,
            new { slug, name = "Test event" })).EnsureSuccess();

        var second = await Send(HttpMethod.Post, "/admin/events", cookie,
            new { slug, name = "Test event" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task The_database_refuses_a_slug_the_endpoint_would_have()
    {
        // The rule that has to survive the next hand-written INSERT, which is
        // how every event before this existed. Checked against the column
        // rather than through the API on purpose.
        await using var cmd = db.DataSource.CreateCommand(
            "INSERT INTO applications.events (slug, name) VALUES ('season/2', 'Test event')");

        var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => cmd.ExecuteNonQueryAsync());

        Assert.Equal("23514", refused.SqlState);
    }

    [Fact]
    public async Task An_update_cannot_rename_the_slug()
    {
        // It is what links are built from, so it is not in the update's shape
        // at all. A caller sending one is ignored rather than obeyed.
        var cookie = await ManagerAsync();
        var slug = Slug();
        var id = await MakeEventAsync(cookie, slug);

        var response = await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { slug = Slug(), name = "Renamed" });
        response.EnsureSuccessStatusCode();

        var body = await ReadAsync(response);
        Assert.Equal(slug, body["slug"]!.GetValue<string>());
        Assert.Equal("Renamed", body["name"]!.GetValue<string>());
    }

    // ---------------------------------------------------------- the dates ---

    [Fact]
    public async Task Dates_arrive_later_by_update()
    {
        // The reason creating takes two fields. Everything below is decided
        // over the weeks after somebody makes the event.
        var cookie = await ManagerAsync();
        var id = await MakeEventAsync(cookie);

        var response = await Send(HttpMethod.Put, $"/admin/events/{id}", cookie, new
        {
            startsAt = "2099-03-06T13:00:00Z",
            endsAt = "2099-03-08T21:00:00Z",
            registrationOpensAt = "2099-01-05T05:00:00Z",
            registrationClosesAt = "2099-02-20T05:00:00Z",
            decisionsAnnouncedAt = "2099-02-25T23:00:00Z",
            capacity = 300,
        });
        response.EnsureSuccessStatusCode();

        var body = await ReadAsync(response);
        Assert.Equal(Instant("2099-03-06T13:00:00Z"), Read(body, "startsAt"));
        Assert.Equal(Instant("2099-03-08T21:00:00Z"), Read(body, "endsAt"));
        Assert.Equal(Instant("2099-01-05T05:00:00Z"), Read(body, "registrationOpensAt"));
        Assert.Equal(Instant("2099-02-20T05:00:00Z"), Read(body, "registrationClosesAt"));
        Assert.Equal(Instant("2099-02-25T23:00:00Z"), Read(body, "decisionsAnnouncedAt"));
        Assert.Equal(300, body["capacity"]!.GetValue<int>());
    }

    [Fact]
    public async Task An_update_leaves_the_fields_it_does_not_name_alone()
    {
        // The failure this shape exists to prevent. A console that saves one
        // field at a time would otherwise wipe the rest of the calendar, and
        // the wipe looks exactly like a successful save.
        var cookie = await ManagerAsync();
        var id = await MakeEventAsync(cookie);

        await (await Send(HttpMethod.Put, $"/admin/events/{id}", cookie, new
        {
            startsAt = "2099-03-06T13:00:00Z",
            registrationOpensAt = "2099-01-05T05:00:00Z",
            capacity = 300,
        })).EnsureSuccess();

        var response = await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { name = "Renamed" });
        response.EnsureSuccessStatusCode();

        var body = await ReadAsync(response);
        Assert.Equal("Renamed", body["name"]!.GetValue<string>());
        Assert.Equal(Instant("2099-03-06T13:00:00Z"), Read(body, "startsAt"));
        Assert.Equal(Instant("2099-01-05T05:00:00Z"), Read(body, "registrationOpensAt"));
        Assert.Equal(300, body["capacity"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_date_can_be_un_decided()
    {
        // Sending null is a different request from not sending the field, and
        // both have to work: deciding a deadline and then withdrawing it is a
        // normal week.
        var cookie = await ManagerAsync();
        var id = await MakeEventAsync(cookie);

        await (await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { registrationClosesAt = "2099-02-20T05:00:00Z", capacity = 300 }))
            .EnsureSuccess();

        var response = await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { registrationClosesAt = (string?)null, capacity = (int?)null });
        response.EnsureSuccessStatusCode();

        var body = await ReadAsync(response);
        Assert.Null(body["registrationClosesAt"]);
        Assert.Null(body["capacity"]);
    }

    [Theory]
    [InlineData("2099-03-06T13:00:00")]  // an instant somewhere, and nobody says where
    [InlineData("2099-03-06")]           // a calendar day, which is not an instant at all
    [InlineData("March 6th")]
    public async Task A_date_with_no_offset_is_refused(string sent)
    {
        // The bug this refuses is silent. Parsed as local time it becomes the
        // machine's midnight — UTC in a container, something else on a laptop —
        // and lands on a different calendar day for exactly the people the
        // date exists for.
        var cookie = await ManagerAsync();
        var id = await MakeEventAsync(cookie);

        var response = await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { registrationOpensAt = sent });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_offset_is_kept_as_the_instant_it_names()
    {
        // Midnight on the 15th in New York and 05:00 UTC on the 15th are the
        // same moment, and the column stores moments. The console converts
        // before it gets here; this is the proof that nothing re-reads it in
        // some other zone afterwards.
        var cookie = await ManagerAsync();
        var id = await MakeEventAsync(cookie);

        var response = await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { registrationOpensAt = "2099-01-15T00:00:00-05:00" });
        response.EnsureSuccessStatusCode();

        Assert.Equal(
            Instant("2099-01-15T05:00:00Z"),
            Read(await ReadAsync(response), "registrationOpensAt"));
    }

    [Fact]
    public async Task An_event_cannot_end_before_it_starts()
    {
        var cookie = await ManagerAsync();
        var id = await MakeEventAsync(cookie);

        var response = await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { startsAt = "2099-03-08T13:00:00Z", endsAt = "2099-03-06T13:00:00Z" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registration_cannot_close_before_it_opens()
    {
        // Measured against the row rather than against this request: the
        // closing date arrives in its own save, weeks after the opening one.
        var cookie = await ManagerAsync();
        var id = await MakeEventAsync(cookie);

        await (await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { registrationOpensAt = "2099-02-05T05:00:00Z" })).EnsureSuccess();

        var response = await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { registrationClosesAt = "2099-01-05T05:00:00Z" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_capacity_is_a_number_of_people(int sent)
    {
        var cookie = await ManagerAsync();
        var id = await MakeEventAsync(cookie);

        var response = await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { capacity = sent });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_event_needs_a_name_after_it_exists_too()
    {
        var cookie = await ManagerAsync();
        var id = await MakeEventAsync(cookie);

        var blank = await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { name = "  " });
        var cleared = await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { name = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, cleared.StatusCode);
    }

    [Fact]
    public async Task Updating_an_event_that_does_not_exist_says_so()
    {
        var cookie = await ManagerAsync();

        var response = await Send(HttpMethod.Put, $"/admin/events/{Guid.NewGuid()}", cookie,
            new { name = "Test event" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------- what already hangs off one ---

    [Fact]
    public async Task An_event_with_a_form_on_it_still_updates()
    {
        // The thing this must not break. Forms, applications and campaign
        // segments all key off an event, and setting a date is not allowed to
        // disturb any of them.
        var cookie = await ManagerAsync(
            Permission.FormsManage.Value, Permission.ApplicationsView.Value);
        var id = await MakeEventAsync(cookie);
        var form = await MakeApplicationFormAsync(cookie, id);

        var response = await Send(HttpMethod.Put, $"/admin/events/{id}", cookie,
            new { startsAt = "2099-03-06T13:00:00Z", capacity = 300 });
        response.EnsureSuccessStatusCode();

        var forms = await Send(HttpMethod.Get, $"/admin/forms?eventId={id}", cookie);
        forms.EnsureSuccessStatusCode();

        Assert.Contains(
            (await ReadAsync(forms))["forms"]!.AsArray(),
            f => f!["id"]!.GetValue<Guid>() == form);
    }

    [Fact]
    public async Task A_second_event_gets_its_own_application_form()
    {
        // The unique index allowing one application form per event is per
        // event, and creating events through an API is the first time anything
        // has leaned on that. A second season has to be able to have its own.
        var cookie = await ManagerAsync(
            Permission.FormsManage.Value, Permission.ApplicationsView.Value);

        var first = await MakeEventAsync(cookie);
        await MakeApplicationFormAsync(cookie, first);

        var second = await MakeEventAsync(cookie);
        var response = await Send(HttpMethod.Post, $"/admin/forms?eventId={second}", cookie,
            new { name = "Application", kind = "application" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // --------------------------------------------------------------- helpers ---

    private static string Slug() => $"season-{Guid.NewGuid():N}"[..24];

    private static DateTimeOffset Instant(string text) => DateTimeOffset.Parse(
        text, System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset? Read(JsonNode body, string field) =>
        body[field] is null ? null : Instant(body[field]!.GetValue<string>());

    /// <summary>An event made through the API, which is now the only way.</summary>
    private async Task<Guid> MakeEventAsync(string? cookie = null, string? slug = null)
    {
        cookie ??= await ManagerAsync();

        var response = await Send(HttpMethod.Post, "/admin/events", cookie,
            new { slug = slug ?? Slug(), name = "Test event" });
        await response.EnsureSuccess();

        return (await ReadAsync(response))["id"]!.GetValue<Guid>();
    }

    private async Task<Guid> MakeApplicationFormAsync(string cookie, Guid eventId)
    {
        var response = await Send(HttpMethod.Post, $"/admin/forms?eventId={eventId}", cookie,
            new { name = "Application", kind = "application" });
        await response.EnsureSuccess();

        return (await ReadAsync(response))["id"]!.GetValue<Guid>();
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    /// <summary>Somebody who may make events, plus whatever else is named.</summary>
    private async Task<string> ManagerAsync(params string[] also) =>
        (await ManagerWithIdAsync(also)).Cookie;

    private async Task<(string Cookie, Guid Person)> ManagerWithIdAsync(
        params string[] also)
    {
        var id = await db.AddPersonAsync($"events-{Guid.NewGuid():N}@example.com");
        await db.GrantAsync(id, Permission.EventsManage.Value);
        foreach (var permission in also)
        {
            await db.GrantAsync(id, permission);
        }

        return (await SignInAsync(id), id);
    }

    /// <summary>An organizer holding exactly the permissions named, and a session.</summary>
    private async Task<string> OrganizerAsync(params string[] permissions)
    {
        var id = await db.AddPersonAsync($"events-{Guid.NewGuid():N}@example.com");
        foreach (var permission in permissions)
        {
            await db.GrantAsync(id, permission);
        }

        return await SignInAsync(id);
    }

    private async Task<string> TeamMemberAsync(string slug)
    {
        var id = await db.AddPersonAsync($"team-{Guid.NewGuid():N}@example.com");
        await db.AddToTeamAsync(id, slug);
        return await SignInAsync(id);
    }

    /// <summary>
    /// Mints a session directly rather than driving a login.
    /// </summary>
    /// <remarks>
    /// These tests are about what a session is permitted to do, not how it was
    /// obtained, and everybody here is an organizer — who signs in through
    /// Google rather than by magic link.
    /// </remarks>
    private async Task<string> SignInAsync(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

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
