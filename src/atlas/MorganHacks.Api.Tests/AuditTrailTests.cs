using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;
using Npgsql;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The permission audit trail, against a real database.
/// </summary>
/// <remarks>
/// Split down the middle on purpose. The first half goes through the API,
/// because that is where the actor comes from and an entry with no actor is
/// most of the way to no entry at all. The second half writes raw SQL and
/// never touches an endpoint, because "every change is recorded as long as it
/// went through the API" is the claim this whole design exists to avoid
/// making — the migration runner seeds a super admin with raw SQL, and a fix
/// during the event will be typed into psql.
/// </remarks>
public class AuditTrailTests(IdentityDatabase db)
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
    public async Task Reading_the_trail_needs_audit_view_and_not_merely_people_view()
    {
        // The trail names every person holding a sensitive permission and when
        // they got it, which is the reconnaissance step for anybody who wants
        // one of those accounts. Somebody trusted to read the people screen is
        // not automatically trusted with that, which is why audit.view exists
        // as a separate permission and only super-admin confers it.
        var reader = await Organizer("reader");
        await db.GrantAsync(reader, Permission.PeopleView.Value);

        var response = await Send(HttpMethod.Get, "/admin/audit", await SignIn(reader));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reading_the_trail_without_a_session_is_refused()
    {
        var response = await _app.CreateClient().GetAsync("/admin/audit");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------- what gets recorded ---

    [Fact]
    public async Task Every_permission_changing_endpoint_leaves_an_entry_naming_who_did_it()
    {
        // One test across all six rather than six tests, because the failure
        // this catches is one endpoint being missed while the others work —
        // and a missing endpoint looks exactly like a passing suite when each
        // endpoint has its own test and one was never written.
        var admin = await SuperAdmin("recorder");
        var email = Unique("recorded");

        var created = await Send(HttpMethod.Post, "/admin/people", admin.Cookie, new { email });
        var target = (await Body(created)).GetProperty("id").GetGuid();

        await Send(HttpMethod.Post, $"/admin/people/{target}/teams", admin.Cookie,
            new { slug = "logistics" });
        await Send(HttpMethod.Post, $"/admin/people/{target}/grants", admin.Cookie,
            new { permission = "applications.export" });
        await Send(HttpMethod.Delete,
            $"/admin/people/{target}/grants/applications.export", admin.Cookie);
        await Send(HttpMethod.Delete,
            $"/admin/people/{target}/teams/logistics", admin.Cookie);
        await Send(HttpMethod.Post, $"/admin/people/{target}/revoke", admin.Cookie);

        var entries = await TrailFor(target, admin.Cookie);

        Assert.Equal(
            new[]
            {
                "organizer.added", "team.joined", "grant.added",
                "grant.removed", "team.left", "person.revoked",
            },
            entries.Select(ActionOf).Reverse());

        // Every one of them names the admin. A trail that records what changed
        // but not who changed it answers the easy half of the question.
        Assert.All(entries, e => Assert.Equal(admin.Id, ActorOf(e)));
    }

    [Fact]
    public async Task An_entry_carries_the_team_or_permission_that_changed()
    {
        // "Their access changed on Tuesday" is not an answer. The whole point
        // of the trail is to reconstruct why somebody can do something, and
        // that needs the thing itself, not just the fact that something moved.
        var admin = await SuperAdmin("detail");
        var judge = await Organizer("judge");
        var sunday = DateTimeOffset.UtcNow.AddDays(7);

        await Send(HttpMethod.Post, $"/admin/people/{judge}/teams", admin.Cookie,
            new { slug = "judge", expiresAt = sunday });
        await Send(HttpMethod.Post, $"/admin/people/{judge}/grants", admin.Cookie,
            new { permission = "applications.view_resume" });

        var entries = await TrailFor(judge, admin.Cookie);
        var grant = entries.Single(e => ActionOf(e) == "grant.added");
        var team = entries.Single(e => ActionOf(e) == "team.joined");

        Assert.Equal("applications.view_resume", TargetOf(grant));
        Assert.Equal("judge", TargetOf(team));
        Assert.Equal(
            sunday.ToUnixTimeSeconds(),
            team.GetProperty("expiresAt").GetDateTimeOffset().ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Retiming_a_membership_keeps_the_expiry_it_used_to_have()
    {
        // The upsert path. "On the judge team until Sunday, actually Monday"
        // is two decisions, and without the old value the second one cannot be
        // read as an extension rather than a shortening — which is exactly the
        // distinction somebody reviewing access afterwards is looking for.
        var admin = await SuperAdmin("retimer");
        var judge = await Organizer("retimed");
        var sunday = DateTimeOffset.UtcNow.AddDays(7);
        var monday = sunday.AddDays(1);

        await Send(HttpMethod.Post, $"/admin/people/{judge}/teams", admin.Cookie,
            new { slug = "judge", expiresAt = sunday });
        await Send(HttpMethod.Post, $"/admin/people/{judge}/teams", admin.Cookie,
            new { slug = "judge", expiresAt = monday });

        var retimed = (await TrailFor(judge, admin.Cookie))
            .Single(e => ActionOf(e) == "team.retimed");

        Assert.Equal(
            sunday.ToUnixTimeSeconds(),
            retimed.GetProperty("detail").GetProperty("previousExpiresAt")
                   .GetDateTimeOffset().ToUnixTimeSeconds());
    }

    [Fact]
    public async Task A_refused_change_records_nothing()
    {
        // Adding an organizer twice is a 409, and a 409 changed nobody's
        // access. An entry for it would make the trail a log of attempts,
        // which is a different and much noisier thing — and would let anybody
        // who can reach the endpoint write into the record.
        var admin = await SuperAdmin("refuser");
        var email = Unique("twice");

        var created = await Send(HttpMethod.Post, "/admin/people", admin.Cookie, new { email });
        var target = (await Body(created)).GetProperty("id").GetGuid();

        var again = await Send(HttpMethod.Post, "/admin/people", admin.Cookie, new { email });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        Assert.Single(await TrailFor(target, admin.Cookie));
    }

    // --------------------------------------------------------- the filters ---

    [Fact]
    public async Task Filtering_by_subject_shows_what_was_done_to_them_and_not_what_they_did()
    {
        // The two directions are genuinely different questions and it is easy
        // to build a screen that quietly answers the wrong one. An admin who
        // has been granting things all week would otherwise fill their own
        // access-review page with their own actions.
        var admin = await SuperAdmin("both-ways");
        var target = await Organizer("subject");

        await Send(HttpMethod.Post, $"/admin/people/{target}/teams", admin.Cookie,
            new { slug = "comms" });

        var aboutTarget = await TrailFor(target, admin.Cookie);
        Assert.All(aboutTarget,
            e => Assert.Equal(target, e.GetProperty("subjectId").GetGuid()));
        Assert.Contains(aboutTarget, e => TargetOf(e) == "comms");

        // The admin did that; it was not done to them, so it is absent from
        // their own page. Their page still has their own super-admin
        // membership, which is why this looks for the specific team rather
        // than for the absence of team.joined entirely.
        Assert.DoesNotContain(
            await TrailFor(admin.Id, admin.Cookie), e => TargetOf(e) == "comms");
    }

    [Fact]
    public async Task Filtering_by_actor_shows_everything_that_person_changed()
    {
        var admin = await SuperAdmin("actor-filter");
        var first = await Organizer("first");
        var second = await Organizer("second");

        await Send(HttpMethod.Post, $"/admin/people/{first}/teams", admin.Cookie,
            new { slug = "comms" });
        await Send(HttpMethod.Post, $"/admin/people/{second}/teams", admin.Cookie,
            new { slug = "logistics" });

        var theirs = await Entries($"/admin/audit?actor={admin.Id}", admin.Cookie);

        Assert.Equal(2, theirs.Count(e => ActionOf(e) == "team.joined"));
        Assert.All(theirs, e => Assert.Equal(admin.Id, ActorOf(e)));
    }

    [Fact]
    public async Task The_trail_never_carries_an_address()
    {
        // Rule one of this whole system: person ids, never PII. The trail is
        // the file most likely to be exported for an access review and handed
        // to somebody who is not an organizer, so it is the last place an
        // address should be able to reach.
        var admin = await SuperAdmin("no-pii");
        var email = Unique("private");

        await Send(HttpMethod.Post, "/admin/people", admin.Cookie, new { email });

        var response = await Send(HttpMethod.Get, "/admin/audit", admin.Cookie);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(email, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", raw);
    }

    // ------------------------------------------- changes made by raw SQL ---

    [Fact]
    public async Task A_grant_written_by_hand_still_lands_in_the_trail()
    {
        // The one that decides between a trigger and an INSERT in C#. Before
        // the trigger this succeeded silently and wrote nothing, which does
        // not leave a gap in the trail — it leaves a trail that is complete
        // and wrong, and nobody can tell afterwards.
        var person = await Organizer("by-hand");

        await db.GrantAsync(person, Permission.ApplicationsExport.Value);

        var entries = await RawTrailFor(person);

        // Two, because putting them on the allowlist was itself a raw INSERT
        // this fixture made — so this test happens to prove the point twice.
        Assert.Equal(new[] { "organizer.added", "grant.added" },
            entries.Select(e => e.Action));
        Assert.Equal("applications.export", entries[^1].Target);
    }

    [Fact]
    public async Task A_change_made_by_hand_records_no_actor()
    {
        // Honest rather than tidy, and the same choice status history made. A
        // row written in psql genuinely has nobody behind it; a null actor is
        // how you know that is what happened, and inventing a service account
        // would hide the one detail worth seeing.
        var person = await Organizer("anonymous");

        await db.AddToTeamAsync(person, "comms");

        Assert.All(await RawTrailFor(person), e => Assert.Null(e.ActorId));
    }

    [Fact]
    public async Task A_membership_deleted_by_hand_still_lands_in_the_trail()
    {
        // Removals matter more than additions here. "Why did their access
        // stop" is asked during an incident, and a DELETE run by hand is
        // exactly how access stops during one.
        var person = await Organizer("deleted-by-hand");
        await db.AddToTeamAsync(person, "logistics");

        await Execute(
            """
            DELETE FROM identity.team_members m USING identity.teams t
             WHERE t.id = m.team_id AND m.person_id = @id AND t.slug = 'logistics'
            """,
            ("id", person));

        Assert.Equal(
            new[] { "organizer.added", "team.joined", "team.left" },
            (await RawTrailFor(person)).Select(e => e.Action));
    }

    [Fact]
    public async Task Changing_what_a_team_confers_is_recorded_against_the_team()
    {
        // A baseline change hands a permission to everybody on a team at once
        // without touching a single person's row, and the RBAC doc makes it an
        // UPDATE rather than a code change on purpose — so it is a privilege
        // grant with no deploy, no review and, until this, no record.
        await Execute(
            """
            INSERT INTO identity.team_permissions (team_id, permission)
            SELECT id, 'checkin.view_stats' FROM identity.teams WHERE slug = 'volunteer'
            """);

        await using var cmd = db.DataSource.CreateCommand(
            """
            SELECT action, target FROM audit.entries
             WHERE subject_team = 'volunteer' ORDER BY id DESC LIMIT 1
            """);
        await using var reader = await cmd.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("baseline.added", reader.GetString(0));
        Assert.Equal("checkin.view_stats", reader.GetString(1));
    }

    // --------------------------------------------------------- append-only ---

    [Fact]
    public async Task The_trail_refuses_to_be_edited_or_deleted()
    {
        // A record that can be rewritten by whoever is being audited is not
        // evidence, and the person most motivated to rewrite it has a database
        // connection. Enforced in the database rather than by having no C#
        // method for it, because psql does not call C# methods.
        var person = await Organizer("permanent");
        await db.GrantAsync(person, Permission.SponsorsView.Value);

        var edit = await Assert.ThrowsAsync<PostgresException>(() =>
            Execute("UPDATE audit.entries SET action = 'nothing.happened' WHERE subject_id = @id",
                ("id", person)));
        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            Execute("DELETE FROM audit.entries WHERE subject_id = @id", ("id", person)));

        Assert.Contains("append-only", edit.MessageText);
        Assert.Contains("append-only", delete.MessageText);
        Assert.Equal(2, (await RawTrailFor(person)).Count);
    }

    [Fact]
    public async Task The_trail_refuses_to_be_truncated()
    {
        // TRUNCATE skips row triggers entirely, so without its own guard the
        // append-only rule above is one word long — and TRUNCATE is the fast
        // way to empty a table, which makes it the likely one.
        var error = await Assert.ThrowsAsync<PostgresException>(
            () => Execute("TRUNCATE audit.entries"));

        Assert.Contains("append-only", error.MessageText);
    }

    // ------------------------------------------------------------- helpers ---

    private sealed record RawEntry(string Action, Guid? ActorId, string? Target);

    /// <summary>The trail as the API serves it, filtered to one person.</summary>
    private async Task<JsonElement[]> TrailFor(Guid subject, string cookie) =>
        await Entries($"/admin/audit?subject={subject}", cookie);

    private async Task<JsonElement[]> Entries(string path, string cookie)
    {
        var response = await Send(HttpMethod.Get, path, cookie);
        response.EnsureSuccessStatusCode();
        return (await Body(response)).GetProperty("entries").EnumerateArray().ToArray();
    }

    /// <summary>
    /// The trail read straight from the table, oldest first.
    /// </summary>
    /// <remarks>
    /// Used by the raw-SQL tests so that they depend on nothing the API does.
    /// A test proving the database records a hand-written change should not be
    /// able to pass because an endpoint filled a gap.
    /// </remarks>
    private async Task<IReadOnlyList<RawEntry>> RawTrailFor(Guid subject)
    {
        await using var cmd = db.DataSource.CreateCommand(
            "SELECT action, actor_id, target FROM audit.entries "
            + "WHERE subject_id = @id ORDER BY id");
        cmd.Parameters.AddWithValue("id", subject);

        var entries = new List<RawEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new RawEntry(
                reader.GetString(0),
                await reader.IsDBNullAsync(1) ? null : reader.GetGuid(1),
                await reader.IsDBNullAsync(2) ? null : reader.GetString(2)));
        }

        return entries;
    }

    private async Task Execute(string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = db.DataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private static string ActionOf(JsonElement entry) =>
        entry.GetProperty("action").GetString()!;

    private static string? TargetOf(JsonElement entry) =>
        entry.GetProperty("target").GetString();

    private static Guid? ActorOf(JsonElement entry) =>
        entry.GetProperty("actorId").ValueKind == JsonValueKind.Null
            ? null
            : entry.GetProperty("actorId").GetGuid();

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

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private Task<Guid> Organizer(string prefix) =>
        db.AddPersonAsync(Unique(prefix), "organizer");

    private async Task<(Guid Id, string Cookie)> SuperAdmin(string prefix)
    {
        var id = await Organizer(prefix);
        await db.AddToTeamAsync(id, "super-admin");
        return (id, await SignIn(id));
    }

    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }
}
