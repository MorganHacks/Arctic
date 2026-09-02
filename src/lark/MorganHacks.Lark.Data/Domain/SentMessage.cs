namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// One line of somebody's mail history: what we sent them and what became of
/// it.
/// </summary>
/// <remarks>
/// There is no body on this record, and that is the point rather than an
/// oversight. A rendered body is the decision letter, the personal details in
/// it and whatever else the template pulled in; it stays in <c>notify.*</c>
/// and is read by the worker that sends it and nothing else. What answers "I
/// never got it" is the subject, the date and the delivery outcome.
/// </remarks>
public sealed record SentMessage(
    Guid Id,
    string Subject,
    DateTimeOffset QueuedAt,
    DateTimeOffset? SentAt,
    string Status);

/// <summary>
/// What a delivery status means to the person who was meant to receive it.
/// </summary>
/// <remarks>
/// The stored vocabulary is operational — it distinguishes <c>failed_temp</c>
/// from <c>failed_perm</c> because the retry schedule cares. A recipient does
/// not: they need to know whether it arrived, whether it is still coming, or
/// whether it is not coming and they should do something about it.
/// <para>
/// Kept beside the status values themselves so a new one cannot be added
/// without this file being in the diff.
/// </para>
/// </remarks>
public static class DeliveryView
{
    public static string Describe(string status) => status switch
    {
        // 'sent' means the provider took it and nothing has come back.
        // 'delivered' means the recipient's server confirmed it. Both are
        // "it went" to somebody hunting for an email, and splitting them on
        // this screen only invites the question of what the difference is.
        "sent" or "delivered" => "Delivered",

        "pending" or "sending" => "Sending",

        // Worth naming, because it is the one case where the person can act:
        // a bounce usually means the address is wrong or their mailbox is
        // full, and nothing we do will fix either.
        "bounced" => "Could not be delivered",

        "complained" => "Marked as spam",

        // An unsubscribe or a hard bounce stopped it before it left. Said
        // plainly, because "we did not send this" is the honest answer and it
        // is the one that explains the silence.
        "suppressed" => "Not sent — your address is on our do-not-send list",

        "failed_temp" or "failed_perm" => "Could not be delivered",

        // Anything unrecognised reads as in-flight rather than as an error.
        // A status this file has not been taught about is our gap, and
        // telling somebody their acceptance email failed because of it would
        // be worse than saying nothing definite.
        _ => "Sending",
    };
}
