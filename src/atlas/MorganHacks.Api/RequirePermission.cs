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

    /// <summary>The person this request is acting as, once a gate has run.</summary>
    public static Guid PersonId(this HttpContext http) => (Guid)http.Items["PersonId"]!;
}
