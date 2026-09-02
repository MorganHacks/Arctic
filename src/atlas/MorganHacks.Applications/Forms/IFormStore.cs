namespace MorganHacks.Applications.Forms;

/// <summary>A version of the form, and what state it is in.</summary>
public sealed record FormVersion(
    Guid Id,
    Guid EventId,
    int Version,
    string Status,
    IReadOnlyList<FormField> Fields,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt);

/// <summary>Thrown when a form is not in a state to be published.</summary>
public sealed class FormNotPublishableException(IReadOnlyList<FormProblem> problems)
    : InvalidOperationException(
        $"The form has {problems.Count} problem(s) that must be fixed before publishing.")
{
    public IReadOnlyList<FormProblem> Problems { get; } = problems;
}

public interface IFormStore
{
    /// <summary>The form applicants are currently being shown, if any.</summary>
    Task<FormVersion?> PublishedAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>The draft being edited, creating one if none exists.</summary>
    /// <remarks>
    /// A new draft starts from whatever is published, so editing a live form
    /// means changing it rather than rebuilding it. With nothing published it
    /// starts from MLH's questions, because an empty form means somebody
    /// copying an obligation out of a PDF by hand.
    /// </remarks>
    Task<FormVersion> DraftAsync(Guid eventId, Guid? actorId, CancellationToken ct = default);

    Task SaveDraftAsync(
        Guid eventId, IReadOnlyList<FormField> fields, CancellationToken ct = default);

    /// <summary>
    /// Makes the draft the live form.
    /// </summary>
    /// <remarks>
    /// Retires whatever was published, in one transaction, because a unique
    /// index insists on at most one published form per event — and two live
    /// forms would mean applicants answering different questions with nothing
    /// to say which was current.
    /// </remarks>
    Task<FormVersion> PublishAsync(Guid eventId, Guid? actorId, CancellationToken ct = default);

    Task<IReadOnlyList<FormVersion>> HistoryAsync(Guid eventId, CancellationToken ct = default);
}
