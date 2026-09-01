using MorganHacks.Identity.Services;
using MorganHacks.Observability;
using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api;

/// <summary>
/// Hands a magic link to lark's queue instead of sending it here.
/// </summary>
/// <remarks>
/// Atlas does not send mail. Queueing is a row insert that takes a millisecond
/// and cannot fail because a provider is having a bad afternoon, whereas
/// sending inline would put SES's availability directly in the path of
/// somebody clicking "sign in".
/// <para>
/// It also means a login link goes through the same suppression check,
/// retry schedule and bounce handling as everything else. The stub this
/// replaced skipped all three, and would happily have mailed an address that
/// already hard-bounced.
/// </para>
/// <para>
/// This calls lark's own API rather than writing <c>notify.*</c> from here.
/// Modules talk through their published surface; the schema stays lark's, and
/// if lark ever moves to its own database only this class changes.
/// </para>
/// </remarks>
public sealed class QueuedEmailSender(
    TemplateStore templates,
    MessageQueue queue,
    IHttpContextAccessor http,
    ILogger<QueuedEmailSender> log) : IEmailSender
{
    /// <summary>The template this sender needs to exist. Seeded by migration.</summary>
    public const string TemplateKey = "magic_link";

    public async Task SendMagicLinkAsync(
        Guid personId, string email, string link, CancellationToken ct = default)
    {
        var template = await templates.FindAsync(TemplateKey, ct);
        if (template is null)
        {
            // Loud, and not an exception. A missing template is a deployment
            // problem, and throwing here would turn it into a 500 that tells
            // the caller whether their address exists — which is the one thing
            // this endpoint exists to keep quiet about.
            log.LogError(
                "No '{Key}' template, so no sign-in link was queued.", TemplateKey);
            return;
        }

        await queue.EnqueueTransactionalAsync(
            template, email, personId,
            new Dictionary<string, string> { ["link"] = link },
            http.HttpContext?.CorrelationId(), ct);

        // Counted, so the absence can be alerted on. A healthy request rate
        // against a collapsing consumed rate means mail is not arriving —
        // everything green, nobody able to log in.
        log.LogInformation(
            "Queued a sign-in link for {PersonId}. {event}",
            personId, Events.MagicLinkRequested);
    }
}
