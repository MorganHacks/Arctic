using MorganHacks.Applications.Data;
using MorganHacks.Applications.Services;
using MorganHacks.Identity;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api;

public static class ApplicantAnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapApplicantAnalytics(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/analytics/applicants", Read).RequirePermission(Permission.ApplicationsView);
        return app;
    }

    private static async Task<IResult> Read(
        HttpContext http, IEventStore events, PostgresApplicantAnalyticsStore analytics,
        PermissionService permissions, CancellationToken ct, Guid? eventId = null)
    {
        var all = await events.ListAsync(ct);
        var chosen = eventId.HasValue ? all.FirstOrDefault(item => item.Id == eventId) : all.FirstOrDefault();
        if (eventId.HasValue && chosen is null) return Results.NotFound(new { error = "No such event." });
        var effective = await permissions.ForAsync(http.PersonId(), ct);
        var canViewResponses = effective.Can(Permission.ApplicationsViewResponses);
        var result = chosen is null ? null : await analytics.ReadAsync(chosen.Id, canViewResponses,
            DateOnly.FromDateTime(DateTime.UtcNow), ct);
        return Results.Ok(new { events = all, chosen, analytics = result, canViewResponses });
    }
}
