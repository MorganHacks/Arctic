using Microsoft.Extensions.Logging;

namespace MorganHacks.Identity.Services;

/// <summary>
/// Sends one transactional email.
/// </summary>
/// <remarks>
/// A DI-wired interface rather than a direct call, so the stub below can be
/// swapped for the queue-backed lark sender without touching any caller.
/// </remarks>
public interface IEmailSender
{
    Task SendMagicLinkAsync(string email, string link, CancellationToken ct = default);
}

/// <summary>
/// Writes the link to the log instead of sending it.
/// </summary>
/// <remarks>
/// Exists so login works before lark does. It must not survive to
/// registration opening: it skips the suppression check, so it would happily
/// mail an address that has already hard-bounced and damage the sending
/// reputation the real domain spends weeks warming up.
/// </remarks>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> log) : IEmailSender
{
    public Task SendMagicLinkAsync(string email, string link, CancellationToken ct = default)
    {
        // The address is logged at debug only. Business events elsewhere log
        // person_id rather than PII.
        log.LogInformation("Magic link issued. Link: {Link}", link);
        log.LogDebug("Recipient: {Email}", email);
        return Task.CompletedTask;
    }
}
