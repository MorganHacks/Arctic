using MorganHacks.Identity;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api;

public static class EmailAnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapEmailAnalytics(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/analytics/email", async (EmailAnalyticsStore analytics, CancellationToken ct) =>
            Results.Ok(await analytics.ReadAsync(ct))).RequirePermission(Permission.EmailViewStats);
        app.MapGet("/admin/analytics/email/campaigns", async (
            HttpContext http, EmailAnalyticsStore analytics, PermissionService permissions, CancellationToken ct) =>
        {
            var effective = await permissions.ForAsync(http.PersonId(), ct);
            return Results.Ok(new
            {
                campaigns = await analytics.ReadBestCampaignsAsync(
                effective.Can(Permission.EmailManageTemplates), ct)
            });
        }).RequirePermission(Permission.EmailViewStats);
        return app;
    }
}
