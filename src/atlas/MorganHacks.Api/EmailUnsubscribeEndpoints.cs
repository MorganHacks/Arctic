using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api;

public static class EmailUnsubscribeEndpoints
{
    public static IEndpointRouteBuilder MapEmailUnsubscribe(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/email/unsubscribe/{id:guid}", ["GET", "HEAD"], Show);
        app.MapPost("/email/unsubscribe/{id:guid}", Confirm);
        return app;
    }

    private static async Task<IResult> Show(
        Guid id, HttpContext http, UnsubscribeStore links, MessageQueue queue, CancellationToken ct)
    {
        Protect(http);
        var email = await links.FindEmailAsync(id, ct);
        if (email is null) return Missing();
        return await queue.IsSuppressedAsync(email, transactional: false, ct)
            ? Complete()
            : Page("Unsubscribe from announcements?",
                "You will stop receiving MorganHacks broadcast emails. You will still receive sign-in links and essential account emails.",
                """<form method="post"><button type="submit" name="confirm" value="unsubscribe">Unsubscribe</button></form>""");
    }

    private static async Task<IResult> Confirm(
        Guid id, HttpContext http, UnsubscribeStore links, MessageQueue queue, CancellationToken ct)
    {
        Protect(http);
        var email = await links.FindEmailAsync(id, ct);
        if (email is null) return Missing();
        if (!http.Request.HasFormContentType || http.Request.ContentLength > 1024)
            return Results.BadRequest();
        var form = await http.Request.ReadFormAsync(ct);
        if (form["confirm"] != "unsubscribe") return Results.BadRequest();
        await queue.SuppressAsync(email, "unsubscribed", ct);
        return Complete();
    }

    private static void Protect(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store, private";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        http.Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
    }

    private static IResult Complete() => Page("You are unsubscribed",
        "You will no longer receive MorganHacks broadcast emails. You can close this page.");

    private static IResult Missing() => Page("This link is unavailable",
        "Open the unsubscribe link from a MorganHacks email and try again.", status: StatusCodes.Status404NotFound);

    private static IResult Page(string title, string description, string action = "", int status = StatusCodes.Status200OK) =>
        Results.Content($$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{title}} | MorganHacks</title>
              <style>
                :root { color-scheme: light; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; color: #202124; background: #fafafa; }
                * { box-sizing: border-box; }
                body { margin: 0; min-height: 100svh; display: grid; place-items: center; padding: 24px; }
                main { width: 100%; max-width: 460px; background: white; border: 1px solid #ececec; border-radius: 20px; padding: 36px; }
                .brand { color: #003970; font-size: 14px; font-weight: 600; margin: 0 0 28px; }
                h1 { font-size: 24px; line-height: 1.3; letter-spacing: -.5px; margin: 0 0 14px; }
                p { color: #626875; font-size: 15px; line-height: 1.6; margin: 0; }
                form { margin-top: 28px; }
                button { border: 0; border-radius: 10px; background: #003970; color: white; font: inherit; font-weight: 600; padding: 12px 20px; cursor: pointer; }
                button:hover { background: #002e5a; }
                button:focus-visible { outline: 3px solid #6590bb; outline-offset: 3px; }
              </style>
            </head>
            <body><main><p class="brand">MorganHacks</p><h1>{{title}}</h1><p>{{description}}</p>{{action}}</main></body>
            </html>
            """, "text/html; charset=utf-8", statusCode: status);
}
