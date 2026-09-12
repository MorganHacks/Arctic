using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MorganHacks.Applications.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The hacker portal, against a real database.
/// </summary>
/// <remarks>
/// Every rule worth testing here is about what the endpoint refuses to say:
/// somebody else's application, an internal status, the body of an email. None
/// of those can be checked against a mock, because a mock returns whatever the
/// test told it to and the question is what the SQL actually reaches.
/// </remarks>
public class PortalTests(IdentityDatabase db)
    : IClassFixture<IdentityDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;

    public Task InitializeAsync()
    {
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);

            // Say which features this file needs rather than inheriting whatever
            // features.json currently ships. The portal is off by default, and a
            // suite that reads the default would go red every time somebody moved
            // a switch -- which is the opposite of what a switch is for.
            b.UseSetting("enable_hacker_portal_feature", "true");
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Client() => _app.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    /// <summary>Gives a person a live session and returns their cookie header.</summary>
    /// <remarks>
    /// Minted directly rather than by clicking a link. These tests are about
    /// what a session may see, not about how it was obtained — that is
    /// <c>AuthEndpointTests</c>' subject.
    /// </remarks>
    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    private static HttpRequestMessage Get(string path, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static HttpRequestMessage Patch(string path, string cookie, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static HttpRequestMessage Post(string path, string cookie, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    // ------------------------------------------------------------ fixtures ---

    private async Task<Guid> AddEventAsync(
        DateTimeOffset? startsAt = null, DateTimeOffset? decisionsAnnouncedAt = null)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.events (slug, name, starts_at, decisions_announced_at)
            VALUES (@slug, 'Test event', @startsAt, @announced)
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("slug", $"event-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("startsAt", (object?)startsAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("announced", (object?)decisionsAnnouncedAt ?? DBNull.Value);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// An application belonging to a person, complete enough for any status.
    /// </summary>
    /// <remarks>
    /// The MLH-required fields are filled even for a draft, because the
    /// completeness constraint refuses any status past <c>incomplete</c>
    /// without them — and a helper that can only build drafts cannot set up
    /// most of what is tested here.
    /// </remarks>
    private async Task<Guid> AddApplicationAsync(
        Guid eventId,
        Guid personId,
        ApplicationStatus status = ApplicationStatus.Submitted,
        DateTimeOffset? rsvpDeadline = null)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications
                (event_id, person_id, email, status, rsvp_deadline,
                 first_name, last_name, age, phone, school, level_of_study,
                 country, mlh_coc_agreed_at, mlh_data_sharing_at)
            VALUES (@eventId, @personId, @email, @status, @rsvp,
                    'Ada', 'Lovelace', 20, '+15550000000',
                    'Morgan State University', 'undergraduate-3y',
                    'United States', now(), now())
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("email", Unique("app"));
        cmd.Parameters.AddWithValue("status", status.ToWire());
        cmd.Parameters.AddWithValue("rsvp", (object?)rsvpDeadline ?? DBNull.Value);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<(string Status, string Email, string? First, string? Shirt, string? Diet)>
        RowOf(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT status, email, first_name, shirt_size, dietary_needs
              FROM applications.applications WHERE id = @id
            """);
        cmd.Parameters.AddWithValue("id", applicationId);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.GetString(0), r.GetString(1),
                await r.IsDBNullAsync(2) ? null : r.GetString(2),
                await r.IsDBNullAsync(3) ? null : r.GetString(3),
                await r.IsDBNullAsync(4) ? null : r.GetString(4));
    }

    /// <summary>Queues a real sign-in email, through the real endpoint.</summary>
    /// <remarks>
    /// Rather than inserting a <c>notify.messages</c> row by hand, so the
    /// history under test is the shape atlas genuinely produces — including a
    /// rendered body, which is the thing that must not come back out.
    /// </remarks>
    private async Task QueueAnEmailFor(string email) =>
        (await Client().PostAsJsonAsync("/auth/magic-link", new { email }))
            .EnsureSuccessStatusCode();

    /// <summary>A complete, valid profile body.</summary>
    private static object Profile(
        string first = "Ada",
        string school = "Morgan State University",
        string? shirt = "m",
        string? diet = null) => new
        {
            firstName = first,
            lastName = "Lovelace",
            school,
            shirtSize = shirt,
            dietaryNeeds = diet,
            accessibilityNeeds = (string?)null,
        };

    // ---------------------------------------------------------------- reads ---

    [Fact]
    public async Task Without_a_session_the_portal_says_sign_in_rather_than_forbidden()
    {
        // An applicant holds no permissions, so 403 would be the wrong answer
        // for every one of them. The only thing missing is a session.
        var client = Client();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/portal/me")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized, (await client.GetAsync("/portal/messages")).StatusCode);
    }

    [Fact]
    public async Task An_applicant_sees_their_own_application_in_words_they_can_read()
    {
        var person = await db.AddPersonAsync(Unique("mine"));
        var eventId = await AddEventAsync();
        await AddApplicationAsync(eventId, person);

        var body = await (await Client().SendAsync(Get("/portal/me", await SignIn(person))))
            .Content.ReadAsStringAsync();

        Assert.Contains("Application received", body);
        Assert.Contains("Morgan State University", body);
    }

    [Fact]
    public async Task An_applicant_cannot_read_another_applicants_application()
    {
        // The rule the whole portal rests on. Every query is scoped by the
        // session's person id and nothing takes an id from the request, so
        // there is no url to edit — which is what this proves: a second
        // applicant asking the same endpoint gets their own empty page, not
        // the first one's school and shirt size.
        var theirs = await db.AddPersonAsync(Unique("theirs"));
        var eventId = await AddEventAsync();
        await AddApplicationAsync(eventId, theirs);

        var nosy = await db.AddPersonAsync(Unique("nosy"));

        var response = await Client().SendAsync(Get("/portal/me", await SignIn(nosy)));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("\"application\":null", body.Replace(" ", string.Empty));
        Assert.DoesNotContain("Morgan State University", body);
    }

    [Fact]
    public async Task Somebody_who_has_not_started_still_gets_a_page()
    {
        // 200 with nothing, not 404. They are signed in and this is their
        // portal; not having applied yet is a state of the page, and 404 would
        // read as the portal being broken.
        var person = await db.AddPersonAsync(Unique("empty"));

        var response = await Client().SendAsync(Get("/portal/me", await SignIn(person)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_decision_reads_exactly_like_no_decision_until_it_is_announced()
    {
        // The reason ApplicantView exists. A reviewer decides on Tuesday and
        // the team announces on Friday, and in between every applicant sees
        // the same sentence whatever was decided about them.
        var accepted = await db.AddPersonAsync(Unique("accepted"));
        var waiting = await db.AddPersonAsync(Unique("waiting"));
        var eventId = await AddEventAsync();

        var decided = await AddApplicationAsync(eventId, accepted, ApplicationStatus.Incomplete);
        await Decide(decided, ApplicationStatus.Accepted);
        await AddApplicationAsync(eventId, waiting, ApplicationStatus.Submitted);

        var a = await Read("/portal/me", await SignIn(accepted));
        var b = await Read("/portal/me", await SignIn(waiting));

        Assert.Contains("Application received", a);
        Assert.Contains("Application received", b);
        Assert.DoesNotContain("Accepted", a);
    }

    [Fact]
    public async Task Announcing_decisions_changes_what_the_same_row_says()
    {
        // The other half of the same rule: once the event says decisions are
        // out, the mapping stops hiding them. Nothing about the application
        // row changes — which is the point, because the announcement is a
        // decision about timing rather than about anybody's application.
        var person = await db.AddPersonAsync(Unique("announced"));
        var eventId = await AddEventAsync();
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete);
        await Decide(application, ApplicationStatus.Accepted);

        var cookie = await SignIn(person);
        Assert.DoesNotContain("Accepted", await Read("/portal/me", cookie));

        await AnnounceDecisions(eventId);

        Assert.Contains("Accepted", await Read("/portal/me", cookie));
    }

    [Theory]
    [InlineData(ApplicationStatus.Incomplete)]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.Accepted)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Waitlisted)]
    [InlineData(ApplicationStatus.Confirmed)]
    public async Task No_internal_status_ever_reaches_the_applicant(ApplicationStatus status)
    {
        // Checked against the stored spelling of every status rather than the
        // one under test, because the failure this guards against is somebody
        // adding a field to the response that happens to serialise the enum.
        var person = await db.AddPersonAsync(Unique("wire"));
        var eventId = await AddEventAsync(decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var application = await AddApplicationAsync(eventId, person, ApplicationStatus.Incomplete);
        await Decide(application, status);

        var body = await Read("/portal/me", await SignIn(person));

        foreach (var wire in Enum.GetValues<ApplicationStatus>().Select(s => s.ToWire()))
        {
            Assert.DoesNotContain(wire, body, StringComparison.Ordinal);
        }
    }

    // --------------------------------------------------------------- writes ---

    [Fact]
    public async Task A_profile_edit_is_saved_while_the_application_is_still_open()
    {
        var person = await db.AddPersonAsync(Unique("editor"));
        var eventId = await AddEventAsync();
        var application = await AddApplicationAsync(eventId, person, ApplicationStatus.Submitted);

        var response = await Client().SendAsync(Patch(
            "/portal/profile", await SignIn(person),
            Profile(first: "Grace", shirt: "l", diet: "No peanuts")));

        response.EnsureSuccessStatusCode();

        var row = await RowOf(application);
        Assert.Equal("Grace", row.First);
        Assert.Equal("l", row.Shirt);
        Assert.Equal("No peanuts", row.Diet);
    }

    [Fact]
    public async Task A_profile_edit_changes_nothing_but_the_profile()
    {
        // Extra keys in the body are ignored rather than trusted. The SQL
        // names six columns, so status and email are not reachable from here
        // however the request is shaped.
        var person = await db.AddPersonAsync(Unique("sneaky"));
        var eventId = await AddEventAsync();
        var application = await AddApplicationAsync(eventId, person, ApplicationStatus.Submitted);
        var before = await RowOf(application);

        var response = await Client().SendAsync(Patch(
            "/portal/profile", await SignIn(person),
            new
            {
                firstName = "Grace",
                lastName = "Hopper",
                school = "Morgan State University",
                shirtSize = "m",
                status = "accepted",
                email = "somebody-else@example.com",
            }));

        response.EnsureSuccessStatusCode();

        var after = await RowOf(application);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal("Grace", after.First);
    }

    [Fact]
    public async Task A_profile_edit_never_reaches_another_applicants_row()
    {
        // The write half of the same rule as the read test above. Two
        // applicants, one save, and the other row must be untouched — the
        // failure mode being a WHERE clause that narrows on the event or on
        // nothing at all.
        var mine = await db.AddPersonAsync(Unique("mine"));
        var theirs = await db.AddPersonAsync(Unique("theirs"));
        var eventId = await AddEventAsync();
        await AddApplicationAsync(eventId, mine, ApplicationStatus.Submitted);
        var untouched = await AddApplicationAsync(eventId, theirs, ApplicationStatus.Submitted);

        var response = await Client().SendAsync(Patch(
            "/portal/profile", await SignIn(mine), Profile(first: "Grace")));

        response.EnsureSuccessStatusCode();
        Assert.Equal("Ada", (await RowOf(untouched)).First);
    }

    [Fact]
    public async Task A_decided_application_refuses_the_edit_and_says_why()
    {
        // Refused with a sentence rather than a bare 409. "The field is
        // greyed out and I do not know why" is the email this portal exists
        // to prevent, and the sentence must not say which decision was made.
        var person = await db.AddPersonAsync(Unique("locked"));
        var eventId = await AddEventAsync();
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete);
        await Decide(application, ApplicationStatus.Accepted);

        var response = await Client().SendAsync(Patch(
            "/portal/profile", await SignIn(person), Profile(first: "Grace")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Email us", body);
        Assert.DoesNotContain("accepted", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Ada", (await RowOf(application)).First);
    }

    [Fact]
    public async Task A_reviewer_opening_the_file_does_not_lock_the_applicant_out()
    {
        // under_review reads to an applicant exactly like submitted, on
        // purpose. A form that closed when a reviewer picked the application
        // up would hand that difference back through the one thing the
        // applicant can still see: whether the save button works.
        var person = await db.AddPersonAsync(Unique("reviewing"));
        var eventId = await AddEventAsync();
        await AddApplicationAsync(eventId, person, ApplicationStatus.UnderReview);

        var response = await Client().SendAsync(Patch(
            "/portal/profile", await SignIn(person), Profile(first: "Grace")));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_shirt_size_we_do_not_order_is_refused()
    {
        // This column goes to a printer. Free text produces "M", "medium" and
        // "Mens Medium" in one column and somebody reconciles it by hand.
        var person = await db.AddPersonAsync(Unique("shirt"));
        var eventId = await AddEventAsync();
        await AddApplicationAsync(eventId, person, ApplicationStatus.Submitted);

        var response = await Client().SendAsync(Patch(
            "/portal/profile", await SignIn(person), Profile(shirt: "enormous")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_name_cannot_be_cleared_on_an_application_that_needs_one()
    {
        // The database refuses this on anything past a draft, so allowing the
        // save would only buy the applicant a submit that fails later for a
        // reason they cannot see.
        var person = await db.AddPersonAsync(Unique("nameless"));
        var eventId = await AddEventAsync();
        await AddApplicationAsync(eventId, person, ApplicationStatus.Submitted);

        var response = await Client().SendAsync(Patch(
            "/portal/profile", await SignIn(person), Profile(first: "  ")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ----------------------------------------------------------------- rsvp ---

    /// <summary>
    /// An accepted applicant who has been told so can take their spot.
    /// </summary>
    /// <remarks>
    /// The whole feature in one test: the offer is real, the applicant answers
    /// it themselves, and the row moves. Everything below is about the ways
    /// this must not work.
    /// </remarks>
    [Fact]
    public async Task An_accepted_applicant_can_confirm_their_own_spot()
    {
        var person = await db.AddPersonAsync(Unique("confirming"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete,
            rsvpDeadline: DateTimeOffset.UtcNow.AddDays(7));
        await Decide(application, ApplicationStatus.Accepted);

        var response = await Client().SendAsync(
            Post("/portal/rsvp", await SignIn(person), new { answer = "confirm" }));

        response.EnsureSuccessStatusCode();
        Assert.Equal("confirmed", (await RowOf(application)).Status);
    }

    [Fact]
    public async Task An_accepted_applicant_can_decline_their_own_spot()
    {
        var person = await db.AddPersonAsync(Unique("declining"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete,
            rsvpDeadline: DateTimeOffset.UtcNow.AddDays(7));
        await Decide(application, ApplicationStatus.Accepted);

        var response = await Client().SendAsync(
            Post("/portal/rsvp", await SignIn(person), new { answer = "decline" }));

        response.EnsureSuccessStatusCode();
        Assert.Equal("declined", (await RowOf(application)).Status);
    }

    /// <summary>
    /// Confirming from anywhere but <c>accepted</c> is refused.
    /// </summary>
    /// <remarks>
    /// The list is every other status the lifecycle has, rather than the two
    /// or three that seem plausible. The rule is not "these particular states
    /// are wrong" — it is that <see cref="StatusTransition"/> permits exactly
    /// one route into <c>confirmed</c>, and a handler that grew a second one
    /// would pass a test that only checked the states somebody thought of.
    /// </remarks>
    [Theory]
    [InlineData(ApplicationStatus.Incomplete)]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.UnderReview)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Waitlisted)]
    [InlineData(ApplicationStatus.Confirmed)]
    [InlineData(ApplicationStatus.Declined)]
    [InlineData(ApplicationStatus.Expired)]
    [InlineData(ApplicationStatus.CheckedIn)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public async Task Confirming_from_any_other_status_is_refused(ApplicationStatus status)
    {
        var person = await db.AddPersonAsync(Unique("wrongstate"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete,
            rsvpDeadline: DateTimeOffset.UtcNow.AddDays(7));
        await Decide(application, status);

        var response = await Client().SendAsync(
            Post("/portal/rsvp", await SignIn(person), new { answer = "confirm" }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(status.ToWire(), (await RowOf(application)).Status);
    }

    /// <summary>
    /// The deadline is enforced by the write, not by the page.
    /// </summary>
    /// <remarks>
    /// Posted straight at the endpoint with no screen involved, which is the
    /// case that matters: a portal tab opened before the deadline and
    /// submitted after it looks exactly like this, and so does anybody with
    /// curl. A button that is not rendered stops neither.
    /// </remarks>
    [Fact]
    public async Task A_confirm_after_the_deadline_is_refused_however_it_arrives()
    {
        var person = await db.AddPersonAsync(Unique("late"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-7));
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete,
            rsvpDeadline: DateTimeOffset.UtcNow.AddMinutes(-1));
        await Decide(application, ApplicationStatus.Accepted);

        var cookie = await SignIn(person);
        var response = await Client().SendAsync(
            Post("/portal/rsvp", cookie, new { answer = "confirm" }));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("window to confirm has closed", body);

        // Still accepted rather than expired. Letting the deadline lapse is
        // the hourly job's decision to record, and a refusal that quietly
        // expired the row would be this endpoint deciding it instead.
        Assert.Equal("accepted", (await RowOf(application)).Status);

        // And the screen agrees with the refusal it would get.
        Assert.Contains("\"open\":false", (await Read("/portal/me", cookie))
            .Replace(" ", string.Empty));
    }

    /// <summary>
    /// The screen does not ask for something the endpoint would refuse.
    /// </summary>
    /// <remarks>
    /// The same application as the test above, read rather than written. It
    /// answered a confirm with "the window to confirm has closed" while the
    /// label over the button still said to confirm by a day that had already
    /// gone — one response contradicting itself, and the half an applicant
    /// believes is the half that sounds like an instruction.
    /// </remarks>
    [Fact]
    public async Task A_closed_window_does_not_invite_a_confirmation()
    {
        var person = await db.AddPersonAsync(Unique("toolate"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-7));
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete,
            rsvpDeadline: DateTimeOffset.UtcNow.AddMinutes(-1));
        await Decide(application, ApplicationStatus.Accepted);

        var body = await Read("/portal/me", await SignIn(person));

        Assert.DoesNotContain("confirm by", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Confirmation deadline passed", body);

        // And it still agrees with the refusal a confirm would get, which is
        // the sentence that was right all along.
        Assert.Contains("window to confirm has closed", body);
        Assert.Contains("\"open\":false", body.Replace(" ", string.Empty));

        // Nothing was written to say so. The row is still accepted until the
        // hourly job decides otherwise; this is a screen reading a date, not a
        // second place that expires people.
        Assert.Equal("accepted", (await RowOf(application)).Status);
    }

    /// <summary>
    /// No deadline set is not a closed deadline.
    /// </summary>
    /// <remarks>
    /// <c>rsvp_deadline</c> is nullable with no default, so null is the
    /// ordinary state of the column for most of the year. Reading it as
    /// "closed" would leave an accepted applicant holding a spot they cannot
    /// take because a field nobody knew was required was left empty.
    /// </remarks>
    [Fact]
    public async Task An_accepted_applicant_with_no_deadline_can_still_confirm()
    {
        var person = await db.AddPersonAsync(Unique("nodeadline"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete);
        await Decide(application, ApplicationStatus.Accepted);

        var response = await Client().SendAsync(
            Post("/portal/rsvp", await SignIn(person), new { answer = "confirm" }));

        response.EnsureSuccessStatusCode();
        Assert.Equal("confirmed", (await RowOf(application)).Status);
    }

    /// <summary>
    /// The trail says the applicant did it, not nobody.
    /// </summary>
    /// <remarks>
    /// The reason this endpoint goes through <c>TransitionAsync</c> rather
    /// than writing the column. A status written any other way leaves
    /// <c>actor_id</c> null, which is the honest record of a row somebody
    /// fixed by hand — and once written it cannot be told apart from one, ever.
    /// </remarks>
    [Fact]
    public async Task The_audit_trail_names_the_applicant_who_answered()
    {
        var person = await db.AddPersonAsync(Unique("audited"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete,
            rsvpDeadline: DateTimeOffset.UtcNow.AddDays(7));
        await Decide(application, ApplicationStatus.Accepted);

        (await Client().SendAsync(
            Post("/portal/rsvp", await SignIn(person), new { answer = "confirm" })))
            .EnsureSuccessStatusCode();

        var (from, actor) = await LastHistoryRow(application);

        Assert.Equal("accepted", from);
        Assert.Equal(person, actor);
    }

    /// <summary>
    /// Nobody can answer for somebody else.
    /// </summary>
    /// <remarks>
    /// The write half of the rule the whole portal rests on. There is no field
    /// in the body that names an application, so this sends one anyway — the
    /// failure being a handler that grew a way to accept an id and a check
    /// somebody has to remember to write beside it.
    /// </remarks>
    [Fact]
    public async Task An_applicant_cannot_rsvp_for_another_applicant()
    {
        var theirs = await db.AddPersonAsync(Unique("theirs"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var untouched = await AddApplicationAsync(
            eventId, theirs, ApplicationStatus.Incomplete,
            rsvpDeadline: DateTimeOffset.UtcNow.AddDays(7));
        await Decide(untouched, ApplicationStatus.Accepted);

        var nosy = await db.AddPersonAsync(Unique("nosy"));

        var response = await Client().SendAsync(Post(
            "/portal/rsvp", await SignIn(nosy),
            new { answer = "confirm", applicationId = untouched, personId = theirs }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("accepted", (await RowOf(untouched)).Status);
        Assert.Equal(0, await HistoryActorCount(untouched, nosy));
    }

    /// <summary>
    /// An accepted applicant who has not been told cannot answer, and cannot
    /// tell that they were accepted from being refused.
    /// </summary>
    /// <remarks>
    /// The same rule <see cref="ApplicantView"/> exists for, applied to the
    /// refusal. A reviewer decides on Tuesday and the team announces on
    /// Friday; in between, an accepted applicant who pokes this endpoint must
    /// read exactly what somebody still under review reads.
    /// </remarks>
    [Fact]
    public async Task An_unannounced_decision_cannot_be_answered_or_inferred()
    {
        var eventId = await AddEventAsync();

        var accepted = await db.AddPersonAsync(Unique("quietly-accepted"));
        var decided = await AddApplicationAsync(
            eventId, accepted, ApplicationStatus.Incomplete,
            rsvpDeadline: DateTimeOffset.UtcNow.AddDays(7));
        await Decide(decided, ApplicationStatus.Accepted);

        var waiting = await db.AddPersonAsync(Unique("waiting"));
        await AddApplicationAsync(eventId, waiting, ApplicationStatus.Submitted);

        var mine = await Client().SendAsync(
            Post("/portal/rsvp", await SignIn(accepted), new { answer = "confirm" }));
        var theirs = await Client().SendAsync(
            Post("/portal/rsvp", await SignIn(waiting), new { answer = "confirm" }));

        Assert.Equal(HttpStatusCode.Conflict, mine.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, theirs.StatusCode);

        // Word for word, or the refusal is the announcement.
        Assert.Equal(
            await theirs.Content.ReadAsStringAsync(),
            await mine.Content.ReadAsStringAsync());

        Assert.Equal("accepted", (await RowOf(decided)).Status);
    }

    /// <summary>
    /// The deadline never reaches somebody who has not been told the decision
    /// it belongs to.
    /// </summary>
    /// <remarks>
    /// A date in the response is a decision. Every other field on this route
    /// is gated on the announcement and a bare <c>rsvpDeadline</c> would walk
    /// straight past all of them: nobody sets one on an application they have
    /// not accepted.
    /// </remarks>
    [Fact]
    public async Task An_unannounced_deadline_is_not_in_the_response()
    {
        var person = await db.AddPersonAsync(Unique("dateleak"));
        var eventId = await AddEventAsync();
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete,
            rsvpDeadline: new DateTimeOffset(2099, 3, 4, 5, 6, 7, TimeSpan.Zero));
        await Decide(application, ApplicationStatus.Accepted);

        var body = await Read("/portal/me", await SignIn(person));

        Assert.DoesNotContain("2099", body);
        Assert.Contains("\"deadline\":null", body.Replace(" ", string.Empty));
    }

    /// <summary>
    /// Declining is final, because the lifecycle says so.
    /// </summary>
    /// <remarks>
    /// <c>StatusTransition</c> lists nothing after <c>declined</c>. This is
    /// that decision reaching the applicant: a spot given back has gone to
    /// somebody on the waitlist, and a portal that could quietly take it back
    /// would be promising a place that is no longer ours to give.
    /// </remarks>
    [Fact]
    public async Task A_declined_spot_cannot_be_taken_back_from_the_portal()
    {
        var person = await db.AddPersonAsync(Unique("regret"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete,
            rsvpDeadline: DateTimeOffset.UtcNow.AddDays(7));
        await Decide(application, ApplicationStatus.Accepted);

        var cookie = await SignIn(person);
        (await Client().SendAsync(Post("/portal/rsvp", cookie, new { answer = "decline" })))
            .EnsureSuccessStatusCode();

        var again = await Client().SendAsync(
            Post("/portal/rsvp", cookie, new { answer = "confirm" }));
        var body = await again.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("Email us", body);
        Assert.Equal("declined", (await RowOf(application)).Status);
    }

    [Fact]
    public async Task An_answer_that_is_neither_word_is_refused()
    {
        // The body takes a verb rather than a status, so the stored spelling
        // is not a thing this route accepts either.
        var person = await db.AddPersonAsync(Unique("gibberish"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete,
            rsvpDeadline: DateTimeOffset.UtcNow.AddDays(7));
        await Decide(application, ApplicationStatus.Accepted);

        var cookie = await SignIn(person);

        foreach (var answer in new[] { "maybe", "confirmed", string.Empty })
        {
            var response = await Client().SendAsync(
                Post("/portal/rsvp", cookie, new { answer }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal("accepted", (await RowOf(application)).Status);
    }

    [Fact]
    public async Task Rsvp_without_a_session_says_sign_in()
    {
        var response = await Client().PostAsJsonAsync(
            "/portal/rsvp", new { answer = "confirm" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------- withdraw ---

    /// <summary>
    /// Somebody still waiting on a decision can close their own application.
    /// </summary>
    /// <remarks>
    /// The gap this feature fills, in one test. An RSVP can only be answered
    /// by somebody who was offered a spot; everybody before that point — most
    /// of the list, for most of the season — had no way to tell us they were
    /// out, and every one of them stayed in the count and in the mail.
    /// </remarks>
    [Fact]
    public async Task An_applicant_waiting_on_a_decision_can_withdraw()
    {
        var person = await db.AddPersonAsync(Unique("leaving"));
        var eventId = await AddEventAsync();
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete);
        await Decide(application, ApplicationStatus.UnderReview);

        var response = await Client().SendAsync(
            Post("/portal/withdraw", await SignIn(person), new { }));

        response.EnsureSuccessStatusCode();
        Assert.Equal("withdrawn", (await RowOf(application)).Status);
    }

    /// <summary>
    /// So can somebody who already said they were coming.
    /// </summary>
    /// <remarks>
    /// The expensive case, and the reason this is worth building rather than
    /// leaving to email. A confirmed applicant who goes quiet is a seat the
    /// waitlist never gets, a meal ordered for an empty chair and a shirt in
    /// the wrong size — and until this route existed, telling us was something
    /// only an organizer could act on.
    /// </remarks>
    [Fact]
    public async Task A_confirmed_applicant_can_withdraw_and_free_their_seat()
    {
        var person = await db.AddPersonAsync(Unique("cannot-come"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete);
        await Decide(application, ApplicationStatus.Confirmed);

        var response = await Client().SendAsync(
            Post("/portal/withdraw", await SignIn(person), new { }));

        response.EnsureSuccessStatusCode();
        Assert.Equal("withdrawn", (await RowOf(application)).Status);
    }

    /// <summary>
    /// Withdrawing from a status the lifecycle closes is refused, and the
    /// refusal is a sentence.
    /// </summary>
    /// <remarks>
    /// The list is every status <see cref="StatusTransition"/> allows nothing
    /// out of, plus <c>expired</c>, which allows only an organizer's
    /// reinstatement. The assertion worth making is not the status code: it is
    /// that somebody who just pressed a button is told something they can act
    /// on, because "409" on a screen is what generates the email this portal
    /// exists to prevent.
    /// </remarks>
    [Theory]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Declined)]
    [InlineData(ApplicationStatus.Expired)]
    [InlineData(ApplicationStatus.CheckedIn)]
    public async Task Withdrawing_from_a_closed_application_is_refused_with_a_reason(
        ApplicationStatus status)
    {
        var person = await db.AddPersonAsync(Unique("closed"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete);
        await Decide(application, status);

        var cookie = await SignIn(person);
        var response = await Client().SendAsync(Post("/portal/withdraw", cookie, new { }));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // A whole sentence, not a code and not an empty string. Read out of
        // the body rather than compared to a constant, so the wording stays
        // the team's to change without coming back here.
        var error = ErrorIn(body);
        Assert.EndsWith(".", error);
        Assert.True(error.Length > 20, $"The refusal was not a sentence: '{error}'");

        // And it is the sentence the screen is already showing, rather than
        // the handler's last-resort fallback. The two come from one rule on
        // purpose: a refusal that says only "that did not work" is the state
        // that generates the email, and a page that offered the button anyway
        // is how somebody arrives at it.
        Assert.Contains(error, await Read("/portal/me", cookie));

        Assert.Equal(status.ToWire(), (await RowOf(application)).Status);
    }

    /// <summary>
    /// The trail says the applicant did it, not nobody.
    /// </summary>
    /// <remarks>
    /// The reason this endpoint goes through <c>TransitionAsync</c> rather
    /// than writing the column, and it matters more here than anywhere: a
    /// withdrawal is the one status change with no organizer anywhere near it,
    /// so a null <c>actor_id</c> would leave the trail saying a row was fixed
    /// by hand. Once written that cannot be told apart from one, ever.
    /// </remarks>
    [Fact]
    public async Task The_audit_trail_names_the_applicant_who_withdrew()
    {
        var person = await db.AddPersonAsync(Unique("audited-exit"));
        var eventId = await AddEventAsync();
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete);
        await Decide(application, ApplicationStatus.Submitted);

        (await Client().SendAsync(
            Post("/portal/withdraw", await SignIn(person), new { })))
            .EnsureSuccessStatusCode();

        var (from, actor) = await LastHistoryRow(application);

        Assert.Equal("submitted", from);
        Assert.Equal(person, actor);
    }

    /// <summary>
    /// Asking twice is not an error, and does not happen twice.
    /// </summary>
    /// <remarks>
    /// A double submit, a retried request after a dropped response and a back
    /// button all arrive looking exactly like this. Answering the second one
    /// with a refusal would tell somebody their withdrawal failed at the
    /// moment it did not — and the trail has to keep saying they withdrew
    /// once, because a second row is a second thing they did.
    /// </remarks>
    [Fact]
    public async Task Withdrawing_twice_is_not_an_error_and_is_recorded_once()
    {
        var person = await db.AddPersonAsync(Unique("twice"));
        var eventId = await AddEventAsync();
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete);
        await Decide(application, ApplicationStatus.Submitted);

        var cookie = await SignIn(person);

        (await Client().SendAsync(Post("/portal/withdraw", cookie, new { })))
            .EnsureSuccessStatusCode();
        (await Client().SendAsync(Post("/portal/withdraw", cookie, new { })))
            .EnsureSuccessStatusCode();

        Assert.Equal("withdrawn", (await RowOf(application)).Status);
        Assert.Equal(1, await HistoryCountInto(application, ApplicationStatus.Withdrawn));
    }

    /// <summary>
    /// A decision nobody has been told cannot be withdrawn, or inferred from
    /// being refused.
    /// </summary>
    /// <remarks>
    /// The subtle one, and the reason this endpoint asks about the
    /// announcement at all. The lifecycle allows an accepted or waitlisted
    /// application to be withdrawn and allows a rejected one nothing — so a
    /// route that asked only the table would hand two of the three decided
    /// applicants a working button and refuse the third, days before the team
    /// meant to say anything. Pressing it would be how they found out.
    /// </remarks>
    [Fact]
    public async Task An_unannounced_decision_cannot_be_withdrawn_or_inferred()
    {
        var eventId = await AddEventAsync();
        var bodies = new List<string>();

        foreach (var status in new[]
                 {
                     ApplicationStatus.Accepted, ApplicationStatus.Rejected,
                     ApplicationStatus.Waitlisted,
                 })
        {
            var person = await db.AddPersonAsync(Unique("undisclosed"));
            var application = await AddApplicationAsync(
                eventId, person, ApplicationStatus.Incomplete);
            await Decide(application, status);

            var cookie = await SignIn(person);
            var refusal = await Client().SendAsync(Post("/portal/withdraw", cookie, new { }));

            Assert.Equal(HttpStatusCode.Conflict, refusal.StatusCode);
            Assert.Equal(status.ToWire(), (await RowOf(application)).Status);

            bodies.Add(await refusal.Content.ReadAsStringAsync());

            // And the screen agrees with the refusal it would get, in the same
            // words. A button offered here is the announcement.
            var page = await Read("/portal/me", cookie);
            Assert.Contains(ErrorIn(bodies[^1]), page);
        }

        // Word for word across all three, or the refusal is the decision.
        Assert.Single(bodies.Distinct());
    }

    /// <summary>
    /// Once decisions are out, the same three behave differently — and that is
    /// the lifecycle talking, not this file.
    /// </summary>
    /// <remarks>
    /// The other half of the test above. An accepted applicant who cannot come
    /// is exactly the person this feature is for, and gating on the
    /// announcement would be worthless if it never lifted.
    /// </remarks>
    [Fact]
    public async Task An_announced_acceptance_can_be_withdrawn()
    {
        var person = await db.AddPersonAsync(Unique("announced-exit"));
        var eventId = await AddEventAsync(
            decisionsAnnouncedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete,
            rsvpDeadline: DateTimeOffset.UtcNow.AddDays(7));
        await Decide(application, ApplicationStatus.Accepted);

        (await Client().SendAsync(
            Post("/portal/withdraw", await SignIn(person), new { })))
            .EnsureSuccessStatusCode();

        Assert.Equal("withdrawn", (await RowOf(application)).Status);
    }

    /// <summary>
    /// Nobody can withdraw for somebody else.
    /// </summary>
    /// <remarks>
    /// The route takes no body at all, so this sends one anyway — the failure
    /// being a handler that grew a way to accept an id and a check somebody
    /// has to remember to write beside it. A person with no application of
    /// their own is refused for that reason and no other, and the row they
    /// named keeps both its status and its trail.
    /// </remarks>
    [Fact]
    public async Task An_applicant_cannot_withdraw_another_applicants_application()
    {
        var theirs = await db.AddPersonAsync(Unique("theirs-exit"));
        var eventId = await AddEventAsync();
        var untouched = await AddApplicationAsync(
            eventId, theirs, ApplicationStatus.Incomplete);
        await Decide(untouched, ApplicationStatus.Submitted);

        var nosy = await db.AddPersonAsync(Unique("nosy-exit"));

        var response = await Client().SendAsync(Post(
            "/portal/withdraw", await SignIn(nosy),
            new { applicationId = untouched, personId = theirs }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("submitted", (await RowOf(untouched)).Status);
        Assert.Equal(0, await HistoryActorCount(untouched, nosy));
    }

    /// <summary>
    /// Afterwards, every screen says the same thing.
    /// </summary>
    /// <remarks>
    /// A withdrawal closes three things at once — the status line, the RSVP
    /// panel and the profile form — and all three are read from one projection
    /// so they cannot disagree. The status word is checked because the
    /// applicant reads it, and the internal spelling is checked against the
    /// whole body because this route grew a new field and that is exactly how
    /// an enum reaches a screen.
    /// </remarks>
    [Fact]
    public async Task The_portal_reads_as_closed_once_the_application_is_withdrawn()
    {
        var person = await db.AddPersonAsync(Unique("after"));
        var eventId = await AddEventAsync();
        var application = await AddApplicationAsync(
            eventId, person, ApplicationStatus.Incomplete);
        await Decide(application, ApplicationStatus.Submitted);

        var cookie = await SignIn(person);
        (await Client().SendAsync(Post("/portal/withdraw", cookie, new { })))
            .EnsureSuccessStatusCode();

        var body = (await Read("/portal/me", cookie)).Replace(" ", string.Empty);

        Assert.Contains("\"statusLabel\":\"Withdrawn\"", body);
        Assert.Contains("\"open\":false", body);
        Assert.Contains("\"profileEditable\":false", body);
        Assert.DoesNotContain("\"open\":true", body);

        foreach (var wire in Enum.GetValues<ApplicationStatus>().Select(s => s.ToWire()))
        {
            Assert.DoesNotContain(wire, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Withdrawing_without_a_session_says_sign_in()
    {
        // An applicant holds no permissions, so the only thing that can be
        // missing is a session and 403 would be the wrong answer.
        var response = await Client().PostAsJsonAsync("/portal/withdraw", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Withdrawing_with_no_application_says_so_rather_than_failing()
    {
        var person = await db.AddPersonAsync(Unique("nothing-to-close"));

        var response = await Client().SendAsync(
            Post("/portal/withdraw", await SignIn(person), new { }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("not started an application", await response.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------- messages ---

    [Fact]
    public async Task The_message_history_carries_the_subject_and_never_the_body()
    {
        // Rule four. The subject and the outcome answer "I never got it"; the
        // rendered body is the sign-in link itself here, and a decision letter
        // later. Neither belongs on this route.
        var email = Unique("mail");
        var person = await db.AddPersonAsync(email);
        await QueueAnEmailFor(email);

        var body = await Read("/portal/messages", await SignIn(person));

        Assert.Contains("sign-in link", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sending", body);
        Assert.DoesNotContain("/auth/consume", body);
        Assert.DoesNotContain("expires in 15 minutes", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Somebody_elses_mail_is_not_in_your_history()
    {
        var theirEmail = Unique("theirs");
        await db.AddPersonAsync(theirEmail);
        await QueueAnEmailFor(theirEmail);

        var mine = await db.AddPersonAsync(Unique("mine"));

        var body = await Read("/portal/messages", await SignIn(mine));

        Assert.DoesNotContain(theirEmail, body);
        Assert.Contains("\"messages\":[]", body.Replace(" ", string.Empty));
    }

    // -------------------------------------------------------------- helpers ---

    private async Task<string> Read(string path, string cookie)
    {
        var response = await Client().SendAsync(Get(path, cookie));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Moves an application, through the real transition rules.</summary>
    private async Task Decide(Guid applicationId, ApplicationStatus to)
    {
        if (to == ApplicationStatus.Incomplete)
        {
            return;
        }

        var path = RouteTo(to);
        await using var connection = await db.DataSource.OpenConnectionAsync();

        foreach (var step in path)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "UPDATE applications.applications SET status = @s WHERE id = @id";
            cmd.Parameters.AddWithValue("s", step.ToWire());
            cmd.Parameters.AddWithValue("id", applicationId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// The legal route from a fresh application to the status a test wants.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than jumped to, so the rows these tests read have
    /// the history and the lifecycle timestamps a real application would —
    /// including <c>decided_at</c>, which the triggers only stamp on a genuine
    /// transition.
    /// </remarks>
    private static ApplicationStatus[] RouteTo(ApplicationStatus to) => to switch
    {
        ApplicationStatus.Submitted => [ApplicationStatus.Submitted],
        ApplicationStatus.UnderReview =>
            [ApplicationStatus.Submitted, ApplicationStatus.UnderReview],
        ApplicationStatus.Accepted or ApplicationStatus.Rejected
            or ApplicationStatus.Waitlisted =>
            [ApplicationStatus.Submitted, ApplicationStatus.UnderReview, to],
        ApplicationStatus.Confirmed =>
        [
            ApplicationStatus.Submitted, ApplicationStatus.UnderReview,
            ApplicationStatus.Accepted, ApplicationStatus.Confirmed,
        ],
        _ => [to],
    };

    /// <summary>The newest history row's previous status and who caused it.</summary>
    private async Task<(string? From, Guid? Actor)> LastHistoryRow(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT from_status, actor_id
              FROM applications.status_history
             WHERE application_id = @id
             ORDER BY created_at DESC, id DESC
             LIMIT 1
            """);
        cmd.Parameters.AddWithValue("id", applicationId);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (await r.IsDBNullAsync(0) ? null : r.GetString(0),
                await r.IsDBNullAsync(1) ? null : r.GetGuid(1));
    }

    /// <summary>
    /// How many times this person appears on that application's trail.
    /// </summary>
    /// <remarks>
    /// Zero is the assertion worth making after a refused write: a request
    /// that touched nothing must also have left no mark saying it did.
    /// </remarks>
    private async Task<int> HistoryActorCount(Guid applicationId, Guid actorId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT count(*) FROM applications.status_history
             WHERE application_id = @id AND actor_id = @actor
            """);
        cmd.Parameters.AddWithValue("id", applicationId);
        cmd.Parameters.AddWithValue("actor", actorId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// How many times this application has been moved into a status.
    /// </summary>
    /// <remarks>
    /// One is the assertion after a request that arrived twice: the row has to
    /// end up where it was asked to go, and the trail has to say that happened
    /// once, because a second row is a second thing the applicant did.
    /// </remarks>
    private async Task<int> HistoryCountInto(Guid applicationId, ApplicationStatus to)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT count(*) FROM applications.status_history
             WHERE application_id = @id AND to_status = @to
            """);
        cmd.Parameters.AddWithValue("id", applicationId);
        cmd.Parameters.AddWithValue("to", to.ToWire());
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>The sentence a refusal carried.</summary>
    /// <remarks>
    /// Read out of the body rather than compared against a constant copied
    /// over here. What these tests are about is that a refusal explains
    /// itself; the wording is applicant-facing copy the team signs off, and a
    /// test that pinned it would make changing a word a change to this file.
    /// </remarks>
    private static string ErrorIn(string body) =>
        System.Text.Json.JsonDocument.Parse(body).RootElement
            .GetProperty("error").GetString() ?? string.Empty;

    private async Task AnnounceDecisions(Guid eventId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE applications.events SET decisions_announced_at = now() WHERE id = @id");
        cmd.Parameters.AddWithValue("id", eventId);
        await cmd.ExecuteNonQueryAsync();
    }
}
