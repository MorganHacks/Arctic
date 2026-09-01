using Serilog.Core;
using Serilog.Events;

namespace MorganHacks.Observability;

/// <summary>
/// Redacts sensitive properties on the way out.
/// </summary>
/// <remarks>
/// The rule is to log <c>person_id</c> rather than an address, and that rule
/// mostly holds. This is what happens when it does not — a property named
/// <c>email</c> reaches a log line and leaves as <c>[redacted]</c> instead of
/// as somebody's address sitting in a log aggregator for ninety days.
/// </remarks>
public sealed class RedactingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        foreach (var (name, value) in logEvent.Properties)
        {
            if (Redaction.SensitiveKeys.Contains(name))
            {
                logEvent.AddOrUpdateProperty(
                    new LogEventProperty(name, new ScalarValue(Redaction.Placeholder)));
                continue;
            }

            // An address quoted inside a message — a database error naming the
            // row it rejected is the usual way this happens.
            if (value is ScalarValue { Value: string text }
                && Redaction.Mask(text) is { } masked && masked != text)
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(masked)));
            }
        }
    }
}
