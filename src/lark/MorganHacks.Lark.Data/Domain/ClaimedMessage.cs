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
    string BodyText);
