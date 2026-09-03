using MorganHacks.Identity.Services;
using Npgsql;

namespace MorganHacks.Api;

/// <summary>
/// A way in, on a developer's own machine.
/// </summary>
/// <remarks>
/// Organizer sign-in is Google and only Google, which is right for a deployed
/// environment and left local development needing an OAuth client to open a
/// page. This is the door around that.
/// <para>
/// What it deliberately is not: a bypass. It issues a real session through the
/// same <see cref="SessionService"/> the Google callback uses, so every request
/// after it is authenticated exactly as any other request is. Nothing here
/// weakens the check that reads the cookie — there is no branch anywhere that
/// treats a request as signed in without a session row behind it. The thing
/// that would be dangerous is code that skips that check, and this is the
/// alternative to writing it.
/// </para>
/// <para>
/// It is registered only when the environment is Development. Deployed
/// environments are Staging or Production — set explicitly in Bicep on every
/// container — so the route does not exist there rather than existing and
/// refusing. <c>The_dev_door_does_not_exist_outside_development</c> is the
/// assertion that keeps that true.
/// </para>
/// </remarks>
public static class DevAuthEndpoints
{
    private const string SessionCookie = RequirePermissionExtensions.SessionCookie;

    public static IEndpointRouteBuilder MapDevSignIn(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dev/sign-in", SignIn);
        return app;
    }

    private static async Task<IResult> SignIn(
        HttpContext http,
        NpgsqlDataSource db,
        SessionService sessions,
        ILogger<Program> log,
        string? email,
        string? next,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.BadRequest(new
            {
                error = "Which person? Pass ?email=you@morgan.edu",
            });
        }

        // Asked directly rather than through IIdentityStore, whose lookup is
        // for hackers and filters organizers out. Widening that interface so a
        // development convenience can use it would put a find-anybody-by-email
        // method in front of every caller in the application.
        await using var cmd = db.CreateCommand(
            "SELECT id FROM identity.people "
            + "WHERE lower(email) = lower(@email) AND revoked_at IS NULL");
        cmd.Parameters.AddWithValue("email", email);

        var personId = await cmd.ExecuteScalarAsync(ct) as Guid?;
        if (personId is null)
        {
            // Says what to do rather than only that it failed. The answer is
            // almost always that the database is empty and nobody has been
            // seeded yet, and that is not obvious from "not found".
            return Results.NotFound(new
            {
                error = $"Nobody here with the address {email}.",
                fix = "cd src/atlas && ARCTIC_SUPER_ADMIN_EMAIL="
                    + email
                    + " dotnet run --project MorganHacks.Migrations",
            });
        }

        var token = await sessions.StartAsync(
            personId.Value,
            http.Request.Headers.UserAgent.ToString(),
            http.Connection.RemoteIpAddress?.ToString(),
            ct);

        http.Response.Cookies.Append(SessionCookie, token, new CookieOptions
        {
            HttpOnly = true,

            // Browsers make an exception for localhost, so this stays true
            // rather than becoming a setting that differs from the real one.
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = SessionService.Lifetime,
            Path = "/",
        });

        log.LogWarning(
            "Development sign-in used for {Email}. This route does not exist "
            + "outside Development.", email);

        return Results.Redirect(string.IsNullOrWhiteSpace(next) ? "/" : next);
    }
}
