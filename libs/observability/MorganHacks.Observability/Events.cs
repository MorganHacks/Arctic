namespace MorganHacks.Observability;

/// <summary>
/// Business signals, named once so an alert can be built on them.
/// </summary>
/// <remarks>
/// The failures worth alerting on here are absences, not spikes. If
/// <c>magic_link.requested</c> stays healthy while <c>magic_link.consumed</c>
/// collapses, mail is not arriving: every service is up, every dashboard is
/// green, and nobody can log in. No error rate catches that, because nothing
/// is erroring.
/// <para>
/// Emitted as a property on a log line rather than through a metrics library.
/// A counter needs somewhere to go, and one more thing to run is a worse trade
/// than a field an aggregator can already count.
/// </para>
/// </remarks>
public static class Events
{
    /// <summary>The property these appear under.</summary>
    public const string Property = "event";

    /// <summary>A sign-in link was genuinely queued for somebody.</summary>
    public const string MagicLinkRequested = "magic_link.requested";

    /// <summary>Somebody clicked one and got a session.</summary>
    public const string MagicLinkConsumed = "magic_link.consumed";

    /// <summary>A message was accepted by the provider.</summary>
    public const string MessageSent = "message.sent";

    /// <summary>An address was added to the suppression list.</summary>
    public const string AddressSuppressed = "address.suppressed";
}
