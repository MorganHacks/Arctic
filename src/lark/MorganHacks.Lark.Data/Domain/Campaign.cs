namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// One broadcast, however many people it reaches.
/// </summary>
/// <remarks>
/// <see cref="Segment"/> is the definition, stored verbatim as it arrived and
/// never interpreted here. Deciding what "everyone who was accepted" means
/// requires reading <c>applications.*</c>, which is another module's schema —
/// so lark keeps the document and atlas keeps its meaning. What lark does
/// guarantee is that the document is still there a month later, which is the
/// point of storing it at all.
/// </remarks>
public sealed record Campaign(
    Guid Id,
    string Name,
    string Status,
    Guid TemplateId,
    string TemplateKey,
    string TemplateKind,
    Guid? EventId,
    string? Segment,
    int RecipientCount,
    Guid? CreatedBy,
    Guid? ApprovedBy,
    DateTimeOffset? QueuedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt)
{
    /// <summary>A draft is the only thing that can be sent.</summary>
    public bool IsDraft => Status == "draft";
}

/// <summary>
/// What actually happened to a campaign's messages, counted from the rows.
/// </summary>
/// <remarks>
/// Derived rather than stored. <c>notify.campaigns.status</c> records the
/// intent somebody expressed — queued, cancelled — and nothing moves it on as
/// mail goes out, because lark's send loop knows about messages and has never
/// heard of a campaign. Counting the rows is the only honest answer to "has it
/// gone", and it costs one grouped read of an indexed column.
/// </remarks>
public sealed record CampaignProgress(IReadOnlyDictionary<string, int> ByStatus)
{
    public int Total => ByStatus.Values.Sum();

    public int Of(string status) => ByStatus.TryGetValue(status, out var n) ? n : 0;

    /// <summary>Still owed. What cancelling would stop.</summary>
    public int Pending => Of("pending");

    /// <summary>Handed to the provider or further along. What cancelling cannot take back.</summary>
    public int Gone =>
        Of("sending") + Of("sent") + Of("delivered")
        + Of("bounced") + Of("complained") + Of("failed_perm") + Of("failed_temp");

    public static readonly CampaignProgress None =
        new(new Dictionary<string, int>(StringComparer.Ordinal));
}

/// <summary>
/// One person a broadcast is going to, already rendered.
/// </summary>
/// <remarks>
/// Rendered by the caller rather than here, because rendering needs the merge
/// values and those come out of the segment the caller resolved.
/// <para>
/// <see cref="Suppressed"/> is carried rather than looked up during the write.
/// The row is still written when it is set — a recipient we deliberately did
/// not mail is part of the record of what a campaign did, and dropping them
/// leaves "the segment said 412 and 400 were sent" with no way to find the
/// twelve.
/// </para>
/// </remarks>
public sealed record BroadcastRecipient(
    Guid? PersonId,
    string Email,
    string Subject,
    string BodyHtml,
    string BodyText,
    bool Suppressed);

/// <summary>Why a send was refused, or that it was not.</summary>
public enum QueueResult
{
    /// <summary>The rows were written. This send is the one that queued them.</summary>
    Queued,

    /// <summary>
    /// Somebody already sent it, including the request racing this one.
    /// </summary>
    /// <remarks>
    /// Not an error the caller caused, and not a reason to retry. The campaign
    /// went out once, which is what was asked for.
    /// </remarks>
    AlreadyLeftDraft,

    /// <summary>No campaign with that id.</summary>
    NoSuchCampaign,
}

/// <summary>What a send did, for the response and for the log line.</summary>
public sealed record QueueOutcome(QueueResult Result, int Queued, int Suppressed);
