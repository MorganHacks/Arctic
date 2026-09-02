using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api;

/// <summary>
/// Gates an endpoint on a permission.
/// </summary>
/// <remarks>
/// Endpoints check permissions, never teams. <c>Can(Permission.Decide)</c>
/// rather than <c>IsInTeam("registration")</c>: role checks scattered through
/// endpoints turn every reorganisation into a code change, whereas permission
/// checks make it a data change.
/// </remarks>
public static class RequirePermissionExtensions
{
    public const string SessionCookie = "mh_session";

    public static TBuilder RequirePermission<TBuilder>(
        this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var token = http.Request.Cookies[SessionCookie];

            if (string.IsNullOrEmpty(token))
            {
                return Results.Unauthorized();
            }

            var sessions = http.RequestServices.GetRequiredService<SessionService>();
            var session = await sessions.ValidateAsync(token, http.RequestAborted);

            // Every request revalidates the session against the database.
            // That is the cost of opaque sessions and the reason revocation
            // works on the next request rather than at expiry.
            if (!session.Accepted)
            {
                return Results.Unauthorized();
            }

            var permissions = http.RequestServices.GetRequiredService<PermissionService>();
            var effective = await permissions.ForAsync(session.PersonId, http.RequestAborted);

            if (!effective.Can(permission))
            {
                // A plain 403, not Results.Forbid(). Forbid() delegates to the
                // authentication stack and throws when no scheme is
                // registered — and there is none, because sessions here are
                // our own cookie rather than ASP.NET's auth pipeline.
                //
                // 403 rather than 404: the caller is authenticated, and we are
                // telling them they lack a permission, not hiding the route.
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            http.Items["PersonId"] = session.PersonId;
            return await next(context);
        });

        return builder;
    }

    /// <summary>
    /// Gates an endpoint on a live session and nothing else.
    /// </summary>
    /// <remarks>
    /// For the endpoints an applicant uses. An applicant is not an organizer
    /// and holds no permissions at all — that is the model working, not a gap
    /// in it — so gating their own application on a permission would either
    /// deny everybody or mean inventing a permission granted to the entire
    /// world.
    /// <para>
    /// What replaces the permission check is the scope of the query. Every
    /// handler behind this gate reads and writes by
    /// <see cref="PersonId(HttpContext)"/> and never by an id from the
    /// request, so "signed in" and "may see this row" are the same fact.
    /// Anything behind this gate that takes an id from the caller is a bug.
    /// </para>
    /// </remarks>
    public static TBuilder RequireSession<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var token = http.Request.Cookies[SessionCookie];

            if (string.IsNullOrEmpty(token))
            {
                return Results.Unauthorized();
            }

            var sessions = http.RequestServices.GetRequiredService<SessionService>();
            var session = await sessions.ValidateAsync(token, http.RequestAborted);

            // Revalidated against the database on every request, like the
            // permission gate above it. Revoking a session has to end an
            // applicant's access on the next request too, not at expiry.
            if (!session.Accepted)
            {
                return Results.Unauthorized();
            }

            http.Items["PersonId"] = session.PersonId;
            return await next(context);
        });

        return builder;
    }

    /// <summary>The person this request is acting as, once a gate has run.</summary>
    public static Guid PersonId(this HttpContext http) => (Guid)http.Items["PersonId"]!;
}
