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
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString));
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

    private async Task AnnounceDecisions(Guid eventId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE applications.events SET decisions_announced_at = now() WHERE id = @id");
        cmd.Parameters.AddWithValue("id", eventId);
        await cmd.ExecuteNonQueryAsync();
    }
}
