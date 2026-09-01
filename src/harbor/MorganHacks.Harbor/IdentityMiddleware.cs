namespace MorganHacks.Harbor;

/// <summary>
/// Says who a request belongs to, and never lets the caller say it for them.
/// </summary>
/// <remarks>
/// Harbor says who you are. The service decides whether you may. Permission
/// checks live in atlas next to the code that acts on them, so that atlas is
/// not a service which is only secure while it happens to be behind this one.
/// </remarks>
public sealed class IdentityMiddleware(RequestDelegate next, ILogger<IdentityMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Strip first, unconditionally, before anything else looks at them.
        //
        // This is the impersonation hole. If a caller sends
        // X-Person-Id: <somebody else> and we forward it, we have handed out
        // the ability to act as anyone. Stripping only when a session is
        // absent, or only on some routes, is the same bug with extra steps.
        foreach (var header in IdentityHeaders.CallerMustNotSupply)
        {
            if (context.Request.Headers.Remove(header))
            {
                log.LogWarning(
                    "Stripped caller-supplied {Header}. Correlation {CorrelationId}.",
                    header, context.Items[MorganHacks.Observability.Telemetry.CorrelationIdProperty]);
            }
        }

        // Session validation lands here next: cookie, lookup, attach. Until
        // atlas exposes an internal endpoint for it, harbor forwards the
        // cookie and atlas resolves it. The strip above is what makes that
        // safe either way.
        await next(context);
    }
}
