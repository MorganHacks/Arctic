using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;
using MorganHacks.Observability;

namespace MorganHacks.Api;

public static class EmailTrackingEndpoints
{
    public static IEndpointRouteBuilder MapEmailTracking(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/email/click/{id:guid}", ["GET", "HEAD"], Visit).RequireRateLimiting("email-click");
        app.MapMethods("/email/open/{id:guid}", ["GET", "HEAD"], Open);
        return app;
    }

    private static async Task<IResult> Visit(
        Guid id, HttpContext http, LinkTrackingStore tracking, IConfiguration configuration, EmailCountryLookup countries, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store, private";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

        var destination = await tracking.VisitAsync(id, HttpMethods.IsGet(http.Request.Method), ct, Visitor(http, configuration, countries));
        return destination is not null && EmailLinks.IsWebUrl(destination)
            ? Results.Redirect(destination)
            : Results.NotFound();
    }

    private static async Task<IResult> Open(
        Guid id, HttpContext http, LinkTrackingStore tracking, IConfiguration configuration, EmailCountryLookup countries, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store, private";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        if (HttpMethods.IsGet(http.Request.Method))
            await tracking.RecordOpenAsync(id, Visitor(http, configuration, countries), ct);
        return Results.File(Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"), "image/gif");
    }

    private static EmailVisitor Visitor(HttpContext http, IConfiguration configuration, EmailCountryLookup countries)
    {
        var header = configuration["EmailTracking:CountryHeader"];
        var country = string.IsNullOrWhiteSpace(header)
            ? countries.Find(ClientAddress.ForRateLimit(http, configuration["Network:ProxySecret"]))
            : http.Request.Headers[header].ToString();
        return EmailVisitor.FromUserAgent(http.Request.Headers.UserAgent.ToString(), country);
    }
}
