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
    Task<SendOutcome> SendAsync(ClaimedMessage message, CancellationToken ct = default);
}
