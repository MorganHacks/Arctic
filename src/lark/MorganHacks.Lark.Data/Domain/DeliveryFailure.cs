namespace MorganHacks.Lark.Data.Domain;

/// <summary>Whether a failure is worth trying again.</summary>
public enum FailureClass
{
    /// <summary>Worth retrying: the address is fine, the moment was not.</summary>
    Temporary,

    /// <summary>Never retry, and suppress the address.</summary>
    PermanentAndSuppress,

    /// <summary>Never retry, but do not suppress — our bug, not their address.</summary>
    PermanentOurFault,
}

/// <summary>
/// Decides whether a failed send is retried.
/// </summary>
/// <remarks>
/// The single most important decision in this service. Retrying a hard bounce
/// is how a sending domain gets blocked: providers track bounce rate, and
/// hammering a dead address repeatedly looks exactly like a spammer working a
/// purchased list.
/// <para>
/// Unknown failures are treated as temporary. A wrongly-retried temporary
/// failure costs a few pointless attempts; a wrongly-suppressed address means
/// someone silently never hears from us again, and nobody finds out until they
/// ask why they got no decision.
/// </para>
/// </remarks>
public static class DeliveryFailure
{
    public static FailureClass Classify(int? statusCode, string? providerCode, string? message)
    {
        var text = $"{providerCode} {message}".ToLowerInvariant();

        // Our own render or template error. Terminal, because retrying runs
        // the same broken code — but never suppress, because there is nothing
        // wrong with the recipient.
        if (text.Contains("render") || text.Contains("template not found"))
        {
            return FailureClass.PermanentOurFault;
        }

        // A complaint is permanent immediately. One spam report is enough:
        // the cost of never mailing them again is nothing next to the cost to
        // the sending domain.
        if (text.Contains("complaint") || text.Contains("spam report"))
        {
            return FailureClass.PermanentAndSuppress;
        }

        // Mailbox or domain does not exist.
        if (text.Contains("no such user") || text.Contains("does not exist")
            || text.Contains("user unknown") || text.Contains("mailbox unavailable")
            || text.Contains("nxdomain") || text.Contains("domain not found"))
        {
            return FailureClass.PermanentAndSuppress;
        }

        // Full mailboxes and greylisting are the classic 4xx cases, and they
        // clear on their own.
        if (text.Contains("mailbox full") || text.Contains("over quota")
            || text.Contains("greylist") || text.Contains("try again"))
        {
            return FailureClass.Temporary;
        }

        return statusCode switch
        {
            // 5xx SMTP is permanent by definition, with one exception below.
            >= 500 and < 600 => FailureClass.PermanentAndSuppress,

            // 4xx SMTP is explicitly "try later". This covers 429 throttling
            // too — IsThrottle below is what tells the worker to slow down,
            // the classification is the same either way.
            >= 400 and < 500 => FailureClass.Temporary,

            // Unknown, including no status at all. Temporary on purpose: a
            // wrongly-retried failure costs a few pointless attempts, while a
            // wrongly-suppressed address means somebody silently never hears
            // from us again and nobody finds out until they ask why.
            _ => FailureClass.Temporary,
        };
    }

    /// <summary>True when the provider is asking us to slow the whole worker down.</summary>
    public static bool IsThrottle(int? statusCode, string? providerCode, string? message) =>
        statusCode == 429
        || $"{providerCode} {message}".Contains("throttl", StringComparison.OrdinalIgnoreCase)
        || $"{providerCode} {message}".Contains("rate exceeded", StringComparison.OrdinalIgnoreCase);
}
