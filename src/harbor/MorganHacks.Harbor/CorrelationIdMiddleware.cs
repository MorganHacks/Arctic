namespace MorganHacks.Harbor;

/// <summary>
/// Gives every request an id, and hands it back to the caller.
/// </summary>
/// <remarks>
/// Generated here because harbor is the first thing in the system that sees a
/// request. It is what turns "I never got my acceptance email" into one query
/// rather than four log searches lined up by hand.
/// <para>
/// Returned in the response header so a hacker can paste it into a support
/// message and we can find their exact request.
/// </para>
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var inbound = context.Request.Headers[IdentityHeaders.CorrelationId].ToString();

        // Accept an inbound id so a request keeps its identity across hops,
        // but never trust its shape: an unbounded caller-supplied string ends
        // up in every log line we write.
        var correlationId = IsPlausible(inbound) ? inbound : Guid.NewGuid().ToString("n");

        context.Request.Headers[IdentityHeaders.CorrelationId] = correlationId;
        context.Response.Headers[IdentityHeaders.CorrelationId] = correlationId;
        context.Items[IdentityHeaders.CorrelationId] = correlationId;

        await next(context);
    }

    private static bool IsPlausible(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-');
}
