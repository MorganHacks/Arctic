using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The gate, against a real database and the real seeded team baselines.
/// </summary>
public class PermissionEnforcementTests(IdentityDatabase db)
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

    /// <summary>Gives a person a live session and returns their cookie.</summary>
    /// <remarks>
    /// Mints the session directly rather than driving a login flow. These
    /// tests are about what a session is permitted to do, not about how it was
    /// obtained, and everyone here is an organizer — who cannot get a magic
    /// link, because organizers sign in through Google.
    /// <para>
    /// This helper used to go through /auth/magic-link, which worked only
    /// because that endpoint wrongly issued links to organizers. The tests
    /// were quietly depending on the hole they should have caught.
    /// </para>
    /// </remarks>
    private async Task<string> SignIn(Guid personId)
    {
        using var scope = _app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task No_session_is_unauthorized_not_forbidden()
    {
        var r = await Client().GetAsync("/people");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_person_with_no_team_is_forbidden()
    {
        // Nothing is granted by default. Being able to log in is not being
        // able to do anything.
        var email = Unique("nobody");
        var id = await db.AddPersonAsync(email, "organizer");
        var cookie = await SignIn(id);

        var r = await Client().SendAsync(Request("/people", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task A_super_admin_is_allowed()
    {
        var email = Unique("admin");
        var id = await db.AddPersonAsync(email, "organizer");
        await db.AddToTeamAsync(id, "super-admin");
        var cookie = await SignIn(id);

        var r = await Client().SendAsync(Request("/people", cookie));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Registration_cannot_view_people()
    {
        // registration's baseline covers applications and one email
        // permission. people.view is not in it, and the endpoint says so
        // rather than relying on nobody trying.
        var email = Unique("reg");
        var id = await db.AddPersonAsync(email, "organizer");
        await db.AddToTeamAsync(id, "registration");
        var cookie = await SignIn(id);

        var r = await Client().SendAsync(Request("/people", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task An_expired_team_membership_grants_nothing()
    {
        // The judge case, end to end: access should die on schedule rather
        // than when somebody remembers to remove it.
        var email = Unique("expired");
        var id = await db.AddPersonAsync(email, "organizer");
        await db.AddToTeamAsync(id, "super-admin", DateTimeOffset.UtcNow.AddDays(-1));
        var cookie = await SignIn(id);

        var r = await Client().SendAsync(Request("/people", cookie));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task An_individual_grant_is_enough_on_its_own()
    {
        // Grants layer on top of team baselines, and work without one.
        var email = Unique("granted");
        var id = await db.AddPersonAsync(email, "organizer");
        await db.GrantAsync(id, "people.view");
        var cookie = await SignIn(id);

        var r = await Client().SendAsync(Request("/people", cookie));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Revoking_the_session_locks_the_endpoint_on_the_next_request()
    {
        var email = Unique("revoked");
        var id = await db.AddPersonAsync(email, "organizer");
        await db.AddToTeamAsync(id, "super-admin");
        var cookie = await SignIn(id);

        Assert.Equal(HttpStatusCode.OK,
            (await Client().SendAsync(Request("/people", cookie))).StatusCode);

        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SessionService>()
                .RevokeAllForPersonAsync(id);
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client().SendAsync(Request("/people", cookie))).StatusCode);
    }

    private static HttpRequestMessage Request(string path, string cookie)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, path);
        r.Headers.Add("Cookie", cookie);
        return r;
    }
}
