namespace MorganHacks.Harbor;

/// <summary>
/// The headers harbor sets on a forwarded request to say who it belongs to.
/// </summary>
/// <remarks>
/// Named in one place because the important rule about them — that inbound
/// copies are stripped unconditionally before ours are set — has to cover
/// every one of them, and a list that lives in two files eventually covers
/// only one.
/// </remarks>
public static class IdentityHeaders
{
    public const string PersonId = "X-Person-Id";
    public const string Permissions = "X-Permissions";
    public const string CorrelationId = "X-Correlation-ID";

    /// <summary>
    /// Everything a caller must never be able to set for themselves.
    /// </summary>
    /// <remarks>
    /// The correlation id is deliberately NOT in here: accepting an inbound
    /// one is how a request keeps its identity across services.
    /// </remarks>
    public static readonly string[] CallerMustNotSupply = [PersonId, Permissions];
}
