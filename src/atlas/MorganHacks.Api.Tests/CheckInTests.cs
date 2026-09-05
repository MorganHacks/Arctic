using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MorganHacks.Applications.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The door, against a real database.
/// </summary>
/// <remarks>
/// Both halves in one file because they are one feature and the interesting
/// assertions cross between them: the code the portal shows an applicant is
/// the code the desk redeems, and neither half is worth much if that is only
/// true by inspection.
/// <para>
/// Against a real Postgres rather than a double, because most of what is under
/// test is enforced by the schema. The unique index is what makes a code name
/// exactly one person, the triggers are what fill in <c>checked_in_by</c>, and
/// the row lock in <c>TransitionAsync</c> is what makes two volunteers
/// scanning at once safe. A mock would agree with whatever this file said.
/// </para>
/// </remarks>
public class CheckInTests(IdentityDatabase db)
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

    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    // ------------------------------------------------------------ the code ---

    [Fact]
    public async Task A_confirmed_hacker_is_given_a_code_and_told_what_it_is_for()
    {
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var pass = await Pass(hacker.Cookie);

        Assert.Equal(CheckInCode.Length, pass.GetProperty("code").GetString()!.Length);
        Assert.Contains("Show this when you arrive", pass.GetProperty("explanation").GetString());
        Assert.False(pass.GetProperty("checkedIn").GetBoolean());

        // Three groups of four, and the square. Both are part of the format
        // rather than the screen's idea of it.
        Assert.Contains(' ', pass.GetProperty("display").GetString()!);
        Assert.Equal(21, pass.GetProperty("qr").GetProperty("size").GetInt32());
    }

    [Fact]
    public async Task The_same_code_comes_back_every_time_it_is_asked_for()
    {
        // The reason the code does not rotate. Somebody screenshots this
        // screen on the bus and shows the screenshot at the door with no
        // signal, and that only works if what is on the phone is still what
        // the desk expects.
        var hacker = await Hacker(ApplicationStatus.Confirmed);

        var first = (await Pass(hacker.Cookie)).GetProperty("code").GetString();
        var second = (await Pass(hacker.Cookie)).GetProperty("code").GetString();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Nobody_is_shown_a_code_that_the_door_would_refuse()
    {
        // Honest degradation. A screen that showed a code to somebody who
        // never confirmed would send them to the front of a queue to be turned
        // away, which is worse than the screen saying so on the bus.
        var hacker = await Hacker(ApplicationStatus.Submitted);
        var pass = await Pass(hacker.Cookie);

        Assert.Equal(JsonValueKind.Null, pass.GetProperty("code").ValueKind);
        Assert.Equal(JsonValueKind.Null, pass.GetProperty("qr").ValueKind);
        Assert.Contains("once you have confirmed a spot",
            pass.GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task An_accepted_applicant_learns_nothing_before_decisions_are_out()
    {
        // The same rule ApplicantView keeps, on a screen nobody thinks of as a
        // decision letter. "Confirm your spot and your code appears here" is a
        // decision, said early, to somebody who has not been told.
        var hacker = await Hacker(ApplicationStatus.Accepted);
        var quiet = await Pass(hacker.Cookie);

        Assert.Contains("once you have confirmed a spot",
            quiet.GetProperty("explanation").GetString());

        await AnnounceDecisions(hacker.EventId);
        var announced = await Pass(hacker.Cookie);

        Assert.Contains("Confirm your spot", announced.GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task Somebody_with_no_application_gets_a_page_rather_than_a_404()
    {
        var person = await db.AddPersonAsync(Unique("empty"));
        var pass = await Pass(await SignIn(person));

        Assert.Equal(JsonValueKind.Null, pass.GetProperty("code").ValueKind);
        Assert.Contains("not started an application",
            pass.GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task The_code_never_leaves_the_person_it_belongs_to()
    {
        // The rule the whole portal rests on, applied to the one field here
        // that is worth stealing.
        var mine = await Hacker(ApplicationStatus.Confirmed);
        var theirs = await Hacker(ApplicationStatus.Confirmed);

        var a = (await Pass(mine.Cookie)).GetProperty("code").GetString();
        var b = (await Pass(theirs.Cookie)).GetProperty("code").GetString();

        Assert.NotEqual(a, b);
    }

    // ------------------------------------------------------------ the scan ---

    [Fact]
    public async Task A_valid_code_checks_somebody_in()
    {
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var code = await CodeFor(hacker.Cookie);

        var (status, body) = await Scan(code, await Volunteer());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("checkedIn", body.GetProperty("outcome").GetString());
        Assert.Equal("Checked in.", body.GetProperty("message").GetString());
        Assert.False(body.GetProperty("alreadyCheckedIn").GetBoolean());
        Assert.Equal("checked_in", await StatusOf(hacker.ApplicationId));
    }

    [Fact]
    public async Task The_scan_names_the_person_so_a_forwarded_code_is_no_use()
    {
        // The answer to the one thing a code that never rotates cannot defend
        // against. The volunteer reads this while looking at whoever handed
        // them the phone.
        var hacker = await Hacker(ApplicationStatus.Confirmed);

        var (_, body) = await Scan(await CodeFor(hacker.Cookie), await Volunteer());

        Assert.Equal("Ada Lovelace", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task The_trail_names_the_volunteer_who_scanned_them()
    {
        // The reason this goes through TransitionAsync rather than an UPDATE.
        // That method is the only writer that tells the transaction who is
        // acting, and both the column and the history row are filled in by
        // triggers reading it.
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var volunteer = await VolunteerPerson();

        await Scan(await CodeFor(hacker.Cookie), await SignIn(volunteer));

        Assert.Equal(volunteer, await CheckedInBy(hacker.ApplicationId));

        var (actor, reason) = await LastHistoryRow(hacker.ApplicationId);
        Assert.Equal(volunteer, actor);
        Assert.Equal("Check-in code scanned.", reason);
    }

    [Fact]
    public async Task A_second_scan_lets_them_through_and_writes_nothing()
    {
        // Two volunteers reaching the same person is what a queue does. The
        // second scanner's question is whether this person may come in, and
        // the answer is still yes -- so 200, and the time of the check-in that
        // actually happened rather than the time of the second question.
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var code = await CodeFor(hacker.Cookie);

        var first = await VolunteerPerson();
        var (_, admitted) = await Scan(code, await SignIn(first));
        var history = await HistoryCount(hacker.ApplicationId);

        var (status, again) = await Scan(code, await Volunteer());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("alreadyCheckedIn", again.GetProperty("outcome").GetString());
        Assert.True(again.GetProperty("alreadyCheckedIn").GetBoolean());
        Assert.Contains("Let them through", again.GetProperty("message").GetString());
        Assert.Equal("Ada Lovelace", again.GetProperty("name").GetString());

        Assert.Equal(
            admitted.GetProperty("checkedInAt").GetDateTimeOffset(),
            again.GetProperty("checkedInAt").GetDateTimeOffset());

        // Nothing was written the second time, so the trail keeps naming the
        // volunteer who actually got to them first.
        Assert.Equal(history, await HistoryCount(hacker.ApplicationId));
        Assert.Equal(first, await CheckedInBy(hacker.ApplicationId));
    }

    [Fact]
    public async Task Two_volunteers_scanning_at_the_same_moment_produce_one_check_in()
    {
        // The race the read above cannot cover. Both requests find 'confirmed'
        // and both try; the row lock inside TransitionAsync means one wins and
        // the other re-reads 'checked_in', which is the same answer the
        // sequential second scan gets.
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var code = await CodeFor(hacker.Cookie);

        var one = Scan(code, await Volunteer());
        var two = Scan(code, await Volunteer());
        var results = await Task.WhenAll(one, two);

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));

        var outcomes = results.Select(r => r.Body.GetProperty("outcome").GetString()).ToList();
        Assert.Contains("checkedIn", outcomes);
        Assert.Contains("alreadyCheckedIn", outcomes);

        // One transition, one history row, whichever of them got there first.
        Assert.Equal(1, await HistoryCountSince(hacker.ApplicationId, "checked_in"));
    }

    [Fact]
    public async Task Somebody_who_never_confirmed_is_refused_with_something_to_do_about_it()
    {
        // StatusTransition.Allowed says only 'confirmed' may become
        // 'checked_in'. This is the sentence that turns that rule into
        // something a volunteer in a doorway can act on.
        var hacker = await Hacker(ApplicationStatus.Accepted);

        // Minted by hand, because the portal correctly refuses to show one.
        var code = await PlantCode(hacker.ApplicationId);
        var (status, body) = await Scan(code, await Volunteer());

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("notConfirmed", body.GetProperty("outcome").GetString());
        Assert.Contains("have not confirmed it", body.GetProperty("error").GetString());
        Assert.Contains("organizer", body.GetProperty("error").GetString());
        Assert.Equal("accepted", await StatusOf(hacker.ApplicationId));
    }

    [Theory]
    [InlineData(ApplicationStatus.Declined)]
    [InlineData(ApplicationStatus.Withdrawn)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Submitted)]
    public async Task Anybody_else_is_refused_in_the_same_words(ApplicationStatus status)
    {
        // One sentence for all of them on purpose. A volunteer's screen is
        // held up in front of the person it is about, and "they were rejected"
        // is not a thing to put on it.
        var hacker = await Hacker(status);
        var (code, wire) = (await PlantCode(hacker.ApplicationId), status.ToWire());

        var (result, body) = await Scan(code, await Volunteer());

        Assert.Equal(HttpStatusCode.Conflict, result);
        Assert.Equal(
            "They are not confirmed for this event. Send them to an organizer.",
            body.GetProperty("error").GetString());
        Assert.Equal(wire, await StatusOf(hacker.ApplicationId));
    }

    [Theory]
    [InlineData("ZZZZZZZZZZZZ")]
    [InlineData("not a code")]
    [InlineData("K7QM4XPT9BD2K7QM4XPT9BD2")]
    [InlineData("!!!!!!!!!!!!")]
    public async Task A_code_we_did_not_issue_is_refused(string forged)
    {
        // The forged, the mistyped and the merely wrong all get one answer.
        // Splitting them would tell somebody guessing which guesses were at
        // least the right shape.
        var (status, body) = await Scan(forged, await Volunteer());

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("unknownCode", body.GetProperty("outcome").GetString());
        Assert.Contains("not one of ours", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_code_typed_out_by_hand_still_works()
    {
        // The fallback for when the camera will not focus. What the volunteer
        // can see is three groups of four, so that is what they type, and the
        // characters Crockford leaves out are folded back to the ones it
        // keeps.
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var typed = (await Pass(hacker.Cookie)).GetProperty("display").GetString()!;

        var (status, body) = await Scan(typed.ToLowerInvariant(), await Volunteer());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("checkedIn", body.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task The_portal_says_so_once_they_are_in()
    {
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        await Scan(await CodeFor(hacker.Cookie), await Volunteer());

        var pass = await Pass(hacker.Cookie);

        Assert.True(pass.GetProperty("checkedIn").GetBoolean());
        Assert.Equal("You are checked in", pass.GetProperty("heading").GetString());

        // The code stays on the screen. Taking it away the moment it worked
        // leaves somebody staring at an empty page wondering whether it did.
        Assert.Equal(CheckInCode.Length, pass.GetProperty("code").GetString()!.Length);
    }

    // ------------------------------------------------------------- the gate ---

    [Fact]
    public async Task Scanning_needs_the_permission_and_a_session()
    {
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var code = await CodeFor(hacker.Cookie);

        var response = await Client().PostAsJsonAsync(
            "/admin/check-in/scan", new { code });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // An organizer on no team holds nothing, which is the model working
        // rather than a gap in it.
        var bystander = await db.AddPersonAsync(Unique("bystander"), "organizer");
        var (refused, _) = await Scan(code, await SignIn(bystander));
        Assert.Equal(HttpStatusCode.Forbidden, refused);

        // And an applicant, who is the one person the code belongs to and
        // still may not redeem it.
        var (theirs, _) = await Scan(code, hacker.Cookie);
        Assert.Equal(HttpStatusCode.Forbidden, theirs);

        Assert.Equal("confirmed", await StatusOf(hacker.ApplicationId));
    }

    [Fact]
    public async Task Logistics_holds_it_too()
    {
        // checkin.scan is seeded onto both teams, and the desk is staffed by
        // whoever is free. A test on only one of them would pass while half
        // the people rostered on could not work.
        var hacker = await Hacker(ApplicationStatus.Confirmed);
        var person = await db.AddPersonAsync(Unique("logistics"), "organizer");
        await db.AddToTeamAsync(person, "logistics");

        var (status, _) = await Scan(await CodeFor(hacker.Cookie), await SignIn(person));

        Assert.Equal(HttpStatusCode.OK, status);
    }

    // ------------------------------------------------------------ fixtures ---

    private sealed record Applicant(Guid PersonId, Guid EventId, Guid ApplicationId, string Cookie);

    /// <summary>An applicant with an application in the status a test needs.</summary>
    /// <remarks>
    /// Every one gets their own event, so a test that announces decisions or
    /// checks somebody in cannot change what another test reads.
    /// </remarks>
    private async Task<Applicant> Hacker(ApplicationStatus status)
    {
        var person = await db.AddPersonAsync(Unique("hacker"));

        await using var addEvent = db.DataSource.CreateCommand("""
            INSERT INTO applications.events (slug, name) VALUES (@slug, 'Test event')
            RETURNING id
            """);
        addEvent.Parameters.AddWithValue("slug", $"event-{Guid.NewGuid():N}");
        var eventId = (Guid)(await addEvent.ExecuteScalarAsync())!;

        await using var addApplication = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications
                (event_id, person_id, email, status,
                 first_name, last_name, age, phone, school, level_of_study,
                 country, mlh_coc_agreed_at, mlh_data_sharing_at)
            VALUES (@eventId, @personId, @email, @status,
                    'Ada', 'Lovelace', 20, '+15550000000',
                    'Morgan State University', 'undergraduate-3y',
                    'United States', now(), now())
            RETURNING id
            """);
        addApplication.Parameters.AddWithValue("eventId", eventId);
        addApplication.Parameters.AddWithValue("personId", person);
        addApplication.Parameters.AddWithValue("email", Unique("app"));
        addApplication.Parameters.AddWithValue("status", status.ToWire());
        var applicationId = (Guid)(await addApplication.ExecuteScalarAsync())!;

        return new Applicant(person, eventId, applicationId, await SignIn(person));
    }

    private async Task<Guid> VolunteerPerson()
    {
        var person = await db.AddPersonAsync(Unique("volunteer"), "organizer");
        await db.AddToTeamAsync(person, "volunteer");
        return person;
    }

    private async Task<string> Volunteer() => await SignIn(await VolunteerPerson());

    private async Task<JsonElement> Pass(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/portal/check-in");
        request.Headers.Add("Cookie", cookie);

        var response = await Client().SendAsync(request);
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private async Task<string> CodeFor(string cookie) =>
        (await Pass(cookie)).GetProperty("code").GetString()!;

    private async Task<(HttpStatusCode Status, JsonElement Body)> Scan(string code, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/check-in/scan")
        {
            Content = JsonContent.Create(new { code }),
        };
        request.Headers.Add("Cookie", cookie);

        var response = await Client().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        return (response.StatusCode,
            body.Length == 0
                ? default
                : JsonDocument.Parse(body).RootElement);
    }

    /// <summary>Puts a code on an application the portal would not issue one for.</summary>
    /// <remarks>
    /// So the refusals can be tested at all. Somebody who declined has no way
    /// to obtain a code through the portal, which is the point, and the case
    /// worth covering is the one where they got hold of one anyway.
    /// </remarks>
    private async Task<string> PlantCode(Guid applicationId)
    {
        var code = CheckInCode.Issue();

        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE applications.applications SET check_in_code = @code WHERE id = @id");
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("id", applicationId);
        await cmd.ExecuteNonQueryAsync();

        return code;
    }

    private async Task AnnounceDecisions(Guid eventId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "UPDATE applications.events SET decisions_announced_at = now() WHERE id = @id");
        cmd.Parameters.AddWithValue("id", eventId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string> StatusOf(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT status FROM applications.applications WHERE id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<Guid?> CheckedInBy(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT checked_in_by FROM applications.applications WHERE id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return await cmd.ExecuteScalarAsync() as Guid?;
    }

    private async Task<int> HistoryCount(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM applications.status_history WHERE application_id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> HistoryCountSince(Guid applicationId, string toStatus)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT count(*) FROM applications.status_history
             WHERE application_id = @id AND to_status = @to
            """);
        cmd.Parameters.AddWithValue("id", applicationId);
        cmd.Parameters.AddWithValue("to", toStatus);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<(Guid? Actor, string? Reason)> LastHistoryRow(Guid applicationId)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT actor_id, reason FROM applications.status_history
             WHERE application_id = @id
             ORDER BY created_at DESC, id DESC
             LIMIT 1
            """);
        cmd.Parameters.AddWithValue("id", applicationId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return (await reader.IsDBNullAsync(0) ? null : reader.GetGuid(0),
                await reader.IsDBNullAsync(1) ? null : reader.GetString(1));
    }
}
