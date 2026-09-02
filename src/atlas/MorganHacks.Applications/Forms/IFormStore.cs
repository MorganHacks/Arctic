namespace MorganHacks.Applications.Forms;

/// <summary>A version of a form, and what state it is in.</summary>
public sealed record FormVersion(
    Guid Id,
    Guid FormId,
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
    /// <summary>Creates a form and the code it will be shared as.</summary>
    Task<Form> CreateAsync(
        Guid eventId, string name, string kind, Guid? actorId, CancellationToken ct = default);

    /// <summary>Resolves the code in a URL. Null when no such form exists.</summary>
    /// <remarks>
    /// A closed form still resolves. Somebody following an old link should be
    /// told it has closed, not shown a 404 they will report as broken.
    /// </remarks>
    Task<Form?> ByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>The form behind an id. Null when there is no such form.</summary>
    /// <remarks>
    /// The code is what a link carries and the id is what an admin route
    /// carries, so both are needed. This one exists so a builder route can
    /// answer 404 for a form that is not there — <see cref="DraftAsync"/>
    /// otherwise happily creates a draft hanging off a made-up id, and an
    /// unreachable orphan row is a worse answer than "no such form".
    /// </remarks>
    Task<Form?> ByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Form>> ForEventAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>The version applicants are currently being shown, if any.</summary>
    Task<FormVersion?> PublishedAsync(Guid formId, CancellationToken ct = default);

    /// <summary>The draft being edited, creating one if none exists.</summary>
    /// <remarks>
    /// A new draft starts from whatever is published, so editing a live form
    /// means changing it rather than rebuilding it. An application form with
    /// nothing published starts from MLH's questions, because an empty one
    /// means somebody copying an obligation out of a PDF by hand.
    /// </remarks>
    Task<FormVersion> DraftAsync(Guid formId, Guid? actorId, CancellationToken ct = default);

    Task SaveDraftAsync(
        Guid formId, IReadOnlyList<FormField> fields, CancellationToken ct = default);

    /// <summary>
    /// Makes the draft the live version.
    /// </summary>
    /// <remarks>
    /// Retires whatever was published, in one transaction, because a unique
    /// index insists on at most one published version per form — and two live
    /// versions would mean applicants answering different questions with
    /// nothing to say which was current.
    /// </remarks>
    Task<FormVersion> PublishAsync(Guid formId, Guid? actorId, CancellationToken ct = default);

    Task<IReadOnlyList<FormVersion>> HistoryAsync(Guid formId, CancellationToken ct = default);
}
