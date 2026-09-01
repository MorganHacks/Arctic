using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Sending;

/// <summary>What happened when we handed a message to the provider.</summary>
/// <remarks>
/// Deliberately not an exception for the failure case. A refused send is an
/// ordinary outcome that the queue knows how to record and retry, not
/// something exceptional, and modelling it as a return value is what keeps the
/// classification in one place instead of spread across catch blocks.
/// </remarks>
public sealed record SendOutcome(
    bool Accepted,
    string? ProviderMessageId = null,
    int? StatusCode = null,
    string? ProviderCode = null,
    string? Error = null)
{
    public static SendOutcome Sent(string providerMessageId) => new(true, providerMessageId);

    public static SendOutcome Refused(
        string error, int? statusCode = null, string? providerCode = null) =>
        new(false, StatusCode: statusCode, ProviderCode: providerCode, Error: error);
}

/// <summary>The thing that actually talks to a mail provider.</summary>
public interface IEmailProvider
{
    /// <summary>
    /// Whether this provider has what it needs to send.
    /// </summary>
    /// <remarks>
    /// Asked before anything is claimed rather than discovered on the way out.
    /// A worker that claims a message it cannot send either burns one of its
    /// five attempts on a problem no retry fixes, or strands the row in
    /// 'sending' until the sweeper takes it back — and repeats that forever.
    /// Not claiming at all leaves the queue exactly as it was, so the moment
    /// credentials arrive the backlog goes out untouched.
    /// </remarks>
    bool IsConfigured { get; }

    Task<SendOutcome> SendAsync(ClaimedMessage message, CancellationToken ct = default);
}

/// <summary>Stands in when no mail provider is configured.</summary>
/// <remarks>
/// Registered instead of letting the SES client throw at startup. A worker
/// that cannot construct its dependencies exits, and a container that exits
/// restarts, so a missing environment variable becomes a crash loop that
/// reports nothing useful — and every other service deploys around it looking
/// healthy.
/// <para>
/// This runs, says exactly what is missing, and sends nothing.
/// </para>
/// </remarks>
public sealed class UnconfiguredEmailProvider(ILogger<UnconfiguredEmailProvider> log)
    : IEmailProvider
{
    public bool IsConfigured => false;

    public Task<SendOutcome> SendAsync(ClaimedMessage message, CancellationToken ct = default)
    {
        log.LogError("Asked to send with no mail provider configured.");
        return Task.FromResult(SendOutcome.Refused("no mail provider configured"));
    }
}
