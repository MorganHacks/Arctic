using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MorganHacks.Identity.Data;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The admin screens' API, against a real database and the real seeded team
/// baselines.
/// </summary>
/// <remarks>
/// Everything here is a write, and every write changes who can read applicant
/// PII. The gate is therefore tested per endpoint rather than once: a filter
/// that is on six routes and missing from the seventh looks exactly like a
/// filter that is on all seven, right up until somebody finds the seventh.
/// </remarks>
public class PeopleAdminTests(IdentityDatabase db)
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

    // ------------------------------------------------------------ the gate ---

    [Fact]
    public async Task Adding_an_organizer_needs_people_manage_teams()
    {
        // people.view is enough to read the screen and deliberately not enough
        // to change it. Reading who has access and handing it out are the two
        // halves the permission model exists to keep apart.
        var reader = await Organizer("reader");
        await db.GrantAsync(reader, Permission.PeopleView.Value);

        var refused = await Send(HttpMethod.Post, "/admin/people", await SignIn(reader),
            new { email = Unique("refused") });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Granting_a_permission_needs_more_than_managing_teams()
    {
        // people.grant_permissions is on the sensitive list because it is
        // privilege escalation: anyone holding it can give themselves anything
        // else. Somebody trusted to run team membership is not automatically
        // trusted with that.
        var manager = await Organizer("manager");
        await db.GrantAsync(manager, Permission.PeopleManageTeams.Value);
        var cookie = await SignIn(manager);
        var target = await Organizer("target");

        var team = await Send(HttpMethod.Post, $"/admin/people/{target}/teams", cookie,
            new { slug = "logistics" });
        var grant = await Send(HttpMethod.Post, $"/admin/people/{target}/grants", cookie,
            new { permission = "applications.export" });

        Assert.Equal(HttpStatusCode.NoContent, team.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
    }

    [Fact]
    public async Task Every_write_refuses_a_caller_with_no_session()
    {
        // Unauthorized rather than Forbidden, and on every route rather than
        // on the one somebody remembered. A write reachable without a session
        // is reachable by the internet.
        var target = await Organizer("stranger");

        foreach (var (method, path) in Writes(target))
        {
            var response = await _app.CreateClient().SendAsync(
                new HttpRequestMessage(method, path));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // ------------------------------------------------------------ revoking ---

    [Fact]
    public async Task Revoking_someone_ends_the_session_they_are_holding_right_now()
    {
        // The whole reason sessions are database rows rather than JWTs. An
        // organizer who leaves badly must not keep a laptop that still lists
        // applicants, and "on their next request" is the only latency this is
        // allowed to have.
        var leaver = await Organizer("leaver");
        await db.AddToTeamAsync(leaver, "super-admin");
        var theirCookie = await SignIn(leaver);

        Assert.Equal(HttpStatusCode.OK,
            (await Send(HttpMethod.Get, "/admin/people", theirCookie)).StatusCode);

        var admin = await SuperAdmin("closer");
        var revoked = await Send(
            HttpMethod.Post, $"/admin/people/{leaver}/revoke", admin.Cookie);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Send(HttpMethod.Get, "/admin/people", theirCookie)).StatusCode);
    }

    [Fact]
    public async Task Revoking_marks_the_sessions_revoked_and_not_only_the_person()
    {
        // The endpoint above would pass even if only revoked_at were set,
        // because session validation joins to people. That makes it a bad
        // witness for the thing that actually matters: the session rows are
        // dead too, so restoring somebody's allowlist entry by mistake does
        // not silently hand their old cookies back.
        var leaver = await Organizer("sessions");
        await SignIn(leaver);
        await SignIn(leaver);
        Assert.Equal(2, await LiveSessions(leaver));

        var admin = await SuperAdmin("closer");
        await Send(HttpMethod.Post, $"/admin/people/{leaver}/revoke", admin.Cookie);

        Assert.Equal(0, await LiveSessions(leaver));
    }

    [Fact]
    public async Task Revoking_yourself_is_refused()
    {
        // It would work exactly as designed — the session dies mid-request and
        // the console logs out — which is why it is refused. Undoing it needs
        // a second admin, and at 2am on event weekend there may not be one
        // awake.
        var admin = await SuperAdmin("self");

        var response = await Send(
            HttpMethod.Post, $"/admin/people/{admin.Id}/revoke", admin.Cookie);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Send(HttpMethod.Get, "/admin/people", admin.Cookie)).StatusCode);
    }

    [Fact]
    public async Task Revoking_twice_keeps_the_moment_access_actually_ended()
    {
        // A second attempt is how an admin finishes a job that half-failed, so
        // it has to be safe to repeat. Rewriting revoked_at each time would
        // destroy the one fact anybody asks for afterwards: when did they stop
        // having access.
        var leaver = await Organizer("twice");
        var admin = await SuperAdmin("closer");

        await Send(HttpMethod.Post, $"/admin/people/{leaver}/revoke", admin.Cookie);
        var first = await RevokedAt(leaver);

        await Send(HttpMethod.Post, $"/admin/people/{leaver}/revoke", admin.Cookie);

        Assert.Equal(first, await RevokedAt(leaver));
    }

    // ------------------------------------------------------- the allowlist ---

    [Fact]
    public async Task A_new_organizer_lands_with_no_permissions_at_all()
    {
        // Self-service onboarding depends on this being unremarkable: being on
        // the allowlist means you can sign in and see nothing, and a super
        // admin decides the rest. An account that arrived useful would make
        // "add them and sort it out later" a privilege grant.
        var admin = await SuperAdmin("adder");
        var email = Unique("fresh");

        var created = await Send(HttpMethod.Post, "/admin/people", admin.Cookie,
            new { email });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var id = (await Body(created)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(HttpMethod.Get, "/admin/people", await SignIn(id))).StatusCode);
    }

    [Fact]
    public async Task An_address_that_already_has_a_hacker_account_is_refused_and_says_why()
    {
        // An organizer account is never also an applicant account. The
        // database enforces it with one index across both kinds, so the only
        // question left is whether the admin is told something they can act
        // on — "use a different address" — or a duplicate-key error.
        var admin = await SuperAdmin("adder");
        var email = Unique("applicant");
        await db.AddPersonAsync(email);

        var response = await Send(HttpMethod.Post, "/admin/people", admin.Cookie,
            new { email });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("hacker account", (await Body(response)).GetProperty("error").GetString()!);
    }

    [Fact]
    public async Task Adding_the_same_organizer_twice_does_not_make_a_second_account()
    {
        var admin = await SuperAdmin("adder");
        var email = Unique("dupe");

        await Send(HttpMethod.Post, "/admin/people", admin.Cookie, new { email });
        var again = await Send(HttpMethod.Post, "/admin/people", admin.Cookie, new { email });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_typo_that_could_never_be_signed_into_is_refused()
    {
        // Nothing here is a security boundary — Google decides whether an
        // address exists. It stops a row that can never match anything sitting
        // on the allowlist looking like somebody has access.
        var admin = await SuperAdmin("adder");

        var response = await Send(HttpMethod.Post, "/admin/people", admin.Cookie,
            new { email = "morgan.edu" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------- teams, grants, expiry ---

    [Fact]
    public async Task Adding_someone_to_a_team_again_retimes_it_rather_than_failing()
    {
        // "On the judge team until Sunday" and "actually, make that Monday"
        // are the same intent twice. Making the second one an error would mean
        // removing a membership in order to shorten it, and a window where the
        // person has neither.
        var admin = await SuperAdmin("timer");
        var judge = await Organizer("judge");
        var sunday = DateTimeOffset.UtcNow.AddDays(7);
        var monday = sunday.AddDays(1);

        await Send(HttpMethod.Post, $"/admin/people/{judge}/teams", admin.Cookie,
            new { slug = "judge", expiresAt = sunday });
        var retimed = await Send(HttpMethod.Post, $"/admin/people/{judge}/teams", admin.Cookie,
            new { slug = "judge", expiresAt = monday });

        Assert.Equal(HttpStatusCode.NoContent, retimed.StatusCode);

        var teams = (await Detail(judge, admin.Cookie)).GetProperty("teams");
        Assert.Equal(1, teams.GetArrayLength());
        Assert.Equal(
            monday.ToUnixTimeSeconds(),
            teams[0].GetProperty("expiresAt").GetDateTimeOffset().ToUnixTimeSeconds());
    }

    [Fact]
    public async Task An_expiry_that_has_already_passed_is_refused()
    {
        // Expiry is inclusive, so a date already gone grants nothing the
        // moment it is written. Accepting it would put a row on the screen
        // that looks like access and is not, and send the admin looking for
        // the bug somewhere else entirely.
        var admin = await SuperAdmin("timer");
        var target = await Organizer("target");
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);

        var team = await Send(HttpMethod.Post, $"/admin/people/{target}/teams", admin.Cookie,
            new { slug = "logistics", expiresAt = yesterday });
        var grant = await Send(HttpMethod.Post, $"/admin/people/{target}/grants", admin.Cookie,
            new { permission = "applications.view", expiresAt = yesterday });

        Assert.Equal(HttpStatusCode.BadRequest, team.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, grant.StatusCode);
    }

    [Fact]
    public async Task A_permission_the_code_does_not_know_is_never_written()
    {
        // The same gate the store uses when reading grants back. A row naming
        // something unrecognised grants nothing but shows on the screen as
        // though it does, which is how an admin concludes access was given
        // when it was not.
        var admin = await SuperAdmin("granter");
        var target = await Organizer("target");

        var response = await Send(HttpMethod.Post, $"/admin/people/{target}/grants",
            admin.Cookie, new { permission = "applications.delete_everything" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await Detail(target, admin.Cookie))
            .GetProperty("grants").GetArrayLength());
    }

    [Fact]
    public async Task Taking_a_grant_away_takes_the_access_with_it()
    {
        var admin = await SuperAdmin("granter");
        var helper = await Organizer("helper");

        await Send(HttpMethod.Post, $"/admin/people/{helper}/grants", admin.Cookie,
            new { permission = "people.view" });
        var theirCookie = await SignIn(helper);
        Assert.Equal(HttpStatusCode.OK,
            (await Send(HttpMethod.Get, "/admin/people", theirCookie)).StatusCode);

        var removed = await Send(HttpMethod.Delete,
            $"/admin/people/{helper}/grants/people.view", admin.Cookie);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(HttpMethod.Get, "/admin/people", theirCookie)).StatusCode);
    }

    [Fact]
    public async Task Leaving_a_team_they_were_never_on_is_a_404_not_a_silent_success()
    {
        // A no-op reported as done is how somebody walks away believing they
        // removed access from a person who still has it.
        var admin = await SuperAdmin("remover");
        var target = await Organizer("target");

        var response = await Send(HttpMethod.Delete,
            $"/admin/people/{target}/teams/comms", admin.Cookie);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------- the detail screen ---

    [Fact]
    public async Task The_effective_list_on_the_screen_is_what_the_gate_actually_allows()
    {
        // The permission doc says the console check is cosmetic and the API is
        // the real gate. That only holds if the two agree: a screen showing a
        // permission the gate refuses sends an organizer to argue with a 403
        // instead of asking for what they need.
        var admin = await SuperAdmin("viewer");
        var person = await Organizer("mixed");
        await db.AddToTeamAsync(person, "logistics");
        await Send(HttpMethod.Post, $"/admin/people/{person}/grants", admin.Cookie,
            new { permission = "email.send_templated" });

        var effective = (await Detail(person, admin.Cookie))
            .GetProperty("effective")
            .EnumerateArray()
            .Select(p => p.GetString()!)
            .ToHashSet();

        using var scope = _app.Services.CreateScope();
        var gate = await scope.ServiceProvider
            .GetRequiredService<PermissionService>().ForAsync(person);

        Assert.Equal(gate.Granted.Select(p => p.Value).ToHashSet(), effective);
        Assert.Contains("checkin.scan", effective);          // from logistics
        Assert.Contains("email.send_templated", effective);  // from the grant
    }

    [Fact]
    public async Task An_expired_membership_shows_on_the_screen_but_grants_nothing()
    {
        // The judge case is the reason both halves matter. Hiding the expired
        // row would make "why did their access stop" unanswerable from the
        // screen; counting it would make the screen lie.
        var admin = await SuperAdmin("viewer");
        var judge = await Organizer("lapsed");
        await db.AddToTeamAsync(judge, "judge", DateTimeOffset.UtcNow.AddDays(-1));

        var detail = await Detail(judge, admin.Cookie);

        Assert.Equal(1, detail.GetProperty("teams").GetArrayLength());
        Assert.Equal(0, detail.GetProperty("effective").GetArrayLength());
    }

    [Fact]
    public async Task A_person_who_does_not_exist_is_a_404_rather_than_an_empty_screen()
    {
        var admin = await SuperAdmin("viewer");

        var response = await Send(
            HttpMethod.Get, $"/admin/people/{Guid.NewGuid()}", admin.Cookie);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_team_list_carries_every_permission_that_exists()
    {
        // The console draws its grant picker from this. If it carried only the
        // permissions some team happens to confer, the ones nobody has yet
        // would be enforced by the API and grantable by nobody.
        var admin = await SuperAdmin("viewer");

        var body = await Body(await Send(HttpMethod.Get, "/admin/teams", admin.Cookie));
        var permissions = body.GetProperty("permissions")
            .EnumerateArray()
            .Select(p => p.GetProperty("value").GetString()!)
            .ToHashSet();
        var sensitive = body.GetProperty("permissions")
            .EnumerateArray()
            .Where(p => p.GetProperty("sensitive").GetBoolean())
            .Select(p => p.GetProperty("value").GetString()!)
            .ToHashSet();

        Assert.Equal(Permission.All.Select(p => p.Value).ToHashSet(), permissions);
        Assert.Equal(Permission.Sensitive.Select(p => p.Value).ToHashSet(), sensitive);
    }

    // ------------------------------------------------------ the store only ---

    [Fact]
    public async Task Revoking_a_person_who_does_not_exist_changes_nothing()
    {
        // The transaction has to roll back rather than commit a session sweep
        // for an id that owns nothing. Harmless today, and the shape that
        // stops being harmless the moment the id is a mistyped one.
        var store = new PostgresIdentityStore(db.DataSource);

        Assert.False(await store.RevokePersonAsync(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), CancellationToken.None));
    }

    // ------------------------------------------------------------- helpers ---

    private static IEnumerable<(HttpMethod, string)> Writes(Guid target) =>
    [
        (HttpMethod.Post, "/admin/people"),
        (HttpMethod.Post, $"/admin/people/{target}/teams"),
        (HttpMethod.Delete, $"/admin/people/{target}/teams/logistics"),
        (HttpMethod.Post, $"/admin/people/{target}/grants"),
        (HttpMethod.Delete, $"/admin/people/{target}/grants/people.view"),
        (HttpMethod.Post, $"/admin/people/{target}/revoke"),
    ];

    private HttpClient Client() => _app.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false });

    private Task<HttpResponseMessage> Send(
        HttpMethod method, string path, string cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return Client().SendAsync(request);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<JsonElement> Detail(Guid personId, string cookie)
    {
        var response = await Send(HttpMethod.Get, $"/admin/people/{personId}", cookie);
        response.EnsureSuccessStatusCode();
        return await Body(response);
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private Task<Guid> Organizer(string prefix) =>
        db.AddPersonAsync(Unique(prefix), "organizer");

    private async Task<(Guid Id, string Cookie)> SuperAdmin(string prefix)
    {
        var id = await Organizer(prefix);
        await db.AddToTeamAsync(id, "super-admin");
        return (id, await SignIn(id));
    }

    /// <summary>Gives a person a live session and returns their cookie.</summary>
    /// <remarks>
    /// Minted directly rather than through a login flow. These tests are about
    /// what a session may do, not how it was obtained, and everyone here is an
    /// organizer — who cannot get a magic link, because organizers sign in
    /// through Google.
    /// </remarks>
    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    private async Task<int> LiveSessions(Guid personId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT count(*) FROM identity.sessions "
            + "WHERE person_id = @id AND revoked_at IS NULL");
        cmd.Parameters.AddWithValue("id", personId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<DateTimeOffset?> RevokedAt(Guid personId)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT revoked_at FROM identity.people WHERE id = @id");
        cmd.Parameters.AddWithValue("id", personId);
        return await cmd.ExecuteScalarAsync() as DateTimeOffset?;
    }
}
