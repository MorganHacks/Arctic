using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Turning a feature off, and what that looks like from outside.
/// </summary>
/// <remarks>
/// The point of a flag is that it can be moved on a bad afternoon without a
/// deploy, by somebody who is not going to read this file first. So the things
/// worth pinning down are the ones that would make that afternoon worse: that
/// the switch actually closes the door, that it closes it on people who are
/// signed in rather than only on strangers, and that the rest of the API is
/// still standing afterwards.
/// </remarks>
public class FeatureFlagTests(IdentityDatabase db)
    : IClassFixture<IdentityDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> _on = null!;
    private WebApplicationFactory<Program> _off = null!;

    public Task InitializeAsync()
    {
        _on = Build(portal: true);
        _off = Build(portal: false);
        return Task.CompletedTask;
    }

    /// <summary>
    /// UseSetting is the same lever the environment variable pulls: both land in
    /// configuration above features.json, which is exactly how the flag is meant
    /// to be moved in a deployed environment.
    /// </summary>
    private WebApplicationFactory<Program> Build(bool portal) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            b.UseSetting("enable_hacker_portal_feature", portal ? "true" : "false");
        });

    public Task DisposeAsync()
    {
        _on.Dispose();
        _off.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<string> SignIn(WebApplicationFactory<Program> app, Guid personId)
    {
        using var scope = app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
        return $"mh_session={await sessions.StartAsync(personId)}";
    }

    private static async Task<HttpStatusCode> GetAsync(
        WebApplicationFactory<Program> app, string path, string cookie)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return (await client.SendAsync(request)).StatusCode;
    }

    [Theory]
    [InlineData("/portal/me")]
    [InlineData("/portal/messages")]
    [InlineData("/portal/check-in")]
    public async Task A_signed_in_applicant_is_told_the_portal_does_not_exist(string path)
    {
        // Signed in, and still 404. A flag that only stopped anonymous callers
        // would leave the portal open to precisely the people who have a link to
        // it, which is everybody it was ever sent to.
        var person = await db.AddPersonAsync($"off-{Guid.NewGuid():N}@example.com");
        var cookie = await SignIn(_off, person);

        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(_off, path, cookie));
    }

    [Fact]
    public async Task Not_forbidden_and_not_unauthorised_but_absent()
    {
        // 403 would say "there is something here and it is not for you"; 401 would
        // send them to sign in, which is a door that leads back to this one. The
        // portal being off is neither. It is not there.
        var person = await db.AddPersonAsync($"shape-{Guid.NewGuid():N}@example.com");
        var cookie = await SignIn(_off, person);

        var status = await GetAsync(_off, "/portal/me", cookie);

        Assert.NotEqual(HttpStatusCode.Forbidden, status);
        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task The_same_request_is_answered_when_the_flag_is_on()
    {
        // The control. Without it a 404 from a broken route would read as the flag
        // working, and this whole file would pass while proving nothing.
        var person = await db.AddPersonAsync($"on-{Guid.NewGuid():N}@example.com");
        var cookie = await SignIn(_on, person);

        Assert.Equal(HttpStatusCode.OK, await GetAsync(_on, "/portal/me", cookie));
    }

    [Fact]
    public async Task Turning_the_portal_off_leaves_the_rest_of_the_API_alone()
    {
        // The flag is scoped to one endpoint group. Somebody flipping it at short
        // notice needs to know that sign-in, and everything the organizers use,
        // are not part of the bargain.
        var person = await db.AddPersonAsync($"rest-{Guid.NewGuid():N}@example.com");
        var cookie = await SignIn(_off, person);

        Assert.Equal(HttpStatusCode.OK, await GetAsync(_off, "/auth/me", cookie));

        var health = await _off.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
