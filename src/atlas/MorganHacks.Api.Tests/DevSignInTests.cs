using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The development door, and the wall it is set into.
/// </summary>
/// <remarks>
/// A way to become somebody without a password is the most dangerous thing in
/// any codebase, and the reason this one is defensible is that it does not
/// weaken authentication at all: it issues a real session through the same
/// service the Google callback uses, and every request afterwards is checked
/// the way any other request is. The danger is not the door — it is the door
/// existing somewhere it should not.
/// <para>
/// So the load-bearing test here is the second one. The first only shows the
/// convenience works.
/// </para>
/// </remarks>
public class DevSignInTests(IdentityDatabase db) : IClassFixture<IdentityDatabase>
{
    private WebApplicationFactory<Program> App(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            b.UseEnvironment(environment);
        });

    private static HttpClient Client(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    [Fact]
    public async Task It_signs_a_known_organizer_in()
    {
        var email = $"dev-{Guid.NewGuid():N}@morgan.edu";
        await db.AddPersonAsync(email, "organizer");

        using var app = App("Development");
        using var http = Client(app);

        var response = await http.GetAsync($"/dev/sign-in?email={Uri.EscapeDataString(email)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // A real session, not a header the rest of the app has been taught to
        // trust. If this cookie stopped being a session the whole point of
        // building it this way would be gone.
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("mh_session=", StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_dev_door_does_not_exist_outside_development()
    {
        // The assertion the feature stands on. Staging and Production are what
        // Bicep sets on every deployed container, and neither may answer here.
        //
        // The address has to be one that exists. An unknown address gets 404
        // from the endpoint itself, which is the same status as the route being
        // absent — the first version of this test used one, passed, and went on
        // passing with the environment guard deleted. A real person is the only
        // input where a live route and a missing one look different: the live
        // one would sign them in.
        var email = $"dev-{Guid.NewGuid():N}@morgan.edu";
        await db.AddPersonAsync(email, "organizer");
        var url = $"/dev/sign-in?email={Uri.EscapeDataString(email)}";

        foreach (var environment in new[] { "Staging", "Production" })
        {
            using var app = App(environment);
            using var http = Client(app);

            var response = await http.GetAsync(url);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain(
                response.Headers.TryGetValues("Set-Cookie", out var set) ? set : [],
                value => value.StartsWith("mh_session=", StringComparison.Ordinal));
        }

        // And the same address does work where the door is meant to be, so the
        // test above is not passing because the address was never usable.
        using var dev = App("Development");
        using var devHttp = Client(dev);

        Assert.Equal(HttpStatusCode.Redirect, (await devHttp.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task An_unknown_address_says_how_to_fix_it()
    {
        using var app = App("Development");
        using var http = Client(app);

        var response = await http.GetAsync("/dev/sign-in?email=nobody@morgan.edu");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("ARCTIC_SUPER_ADMIN_EMAIL", await response.Content.ReadAsStringAsync());
    }
}
