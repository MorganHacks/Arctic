namespace MorganHacks.Observability;

/// <summary>
/// The names every service uses for the same things.
/// </summary>
/// <remarks>
/// Here rather than in each service because a correlation id spelled two ways
/// is a correlation id that correlates nothing, and a redaction list that
/// lives in three files eventually covers one.
/// </remarks>
public static class Telemetry
{
    /// <summary>The header harbor stamps and every other service reads.</summary>
    public const string CorrelationIdHeader = "X-Correlation-ID";

    /// <summary>The property name that id appears under in every log line.</summary>
    public const string CorrelationIdProperty = "CorrelationId";
}
