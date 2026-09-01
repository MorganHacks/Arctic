namespace MorganHacks.Lark.Data.Domain;

/// <summary>A message this worker has taken responsibility for sending.</summary>
public sealed record ClaimedMessage(
    Guid Id,
    Guid CampaignId,
    string ToEmail,
    short Priority,
    short Attempts,
    string Subject,
    string BodyHtml,
    string BodyText,

    // Carried from the template rather than configured on the worker. `kind`
    // decides the sending identity, and a worker-level default is a thing
    // that silently sends a login link from the broadcast domain — which is
    // exactly how login deliverability gets poisoned.
    string From,
    string? ReplyTo);
