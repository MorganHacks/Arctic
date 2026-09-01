using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace MorganHacks.Observability;

/// <summary>
/// Puts the request's correlation id on every log line it produces.
/// </summary>
/// <remarks>
/// One implementation for every service. Harbor is where an id is normally
/// minted, because it is the first thing to see a request; the services behind
/// it accept the id harbor set. Two implementations would eventually disagree
/// about what counts as a plausible id, and an id spelled two ways correlates
/// nothing.
/// <para>
/// An id is generated when the header is absent so that a request arriving
/// some other way — a health check, or somebody hitting the service directly
/// during an incident — is still traceable. Its shape is never trusted: an
/// unbounded caller-supplied string would end up in every log line we write.
/// </para>
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var inbound = context.Request.Headers[Telemetry.CorrelationIdHeader].ToString();
        var correlationId = IsPlausible(inbound) ? inbound : Guid.NewGuid().ToString("n");

        // Written back onto the request as well, so a proxy forwarding it
        // upstream sends the id we settled on rather than the one the caller
        // supplied — which may be the 500-character string rejected above.
        context.Request.Headers[Telemetry.CorrelationIdHeader] = correlationId;

        context.Items[Telemetry.CorrelationIdProperty] = correlationId;

        // Returned to the caller so a hacker can paste it into a support
        // message and we can find their exact request.
        context.Response.Headers[Telemetry.CorrelationIdHeader] = correlationId;

        using (LogContext.PushProperty(Telemetry.CorrelationIdProperty, correlationId))
        {
            await next(context);
        }
    }

    private static bool IsPlausible(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-');
}

public static class CorrelationIdAccessor
{
    /// <summary>The id for this request, for stamping onto anything it creates.</summary>
    public static string? CorrelationId(this HttpContext http) =>
        http.Items.TryGetValue(Telemetry.CorrelationIdProperty, out var value)
            ? value as string
            : null;
}
