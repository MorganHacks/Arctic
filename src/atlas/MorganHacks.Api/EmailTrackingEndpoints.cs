using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Api;

public static class EmailTrackingEndpoints
{
    public static IEndpointRouteBuilder MapEmailTracking(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/email/click/{id:guid}", ["GET", "HEAD"], Visit);
        return app;
    }

    private static async Task<IResult> Visit(
        Guid id, HttpContext http, LinkTrackingStore tracking, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store, private";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

        var destination = await tracking.VisitAsync(id, HttpMethods.IsGet(http.Request.Method), ct);
        return destination is not null && EmailLinks.IsWebUrl(destination)
            ? Results.Redirect(destination)
            : Results.NotFound();
    }
}
