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
    /// <summary>Takes a live form down, leaving its answers and its history.</summary>
    /// <remarks>False when nothing was published, so pressing twice is harmless.</remarks>
    Task<bool> UnpublishAsync(Guid formId, CancellationToken ct = default);

    /// <summary>When the form stops accepting answers. Null means never.</summary>
    Task<Form?> SaveScheduleAsync(
        Guid formId, DateTimeOffset? closesAt, CancellationToken ct = default);

    Task<FormVersion?> PublishedAsync(Guid formId, CancellationToken ct = default);

    /// <summary>The draft being edited, creating one if none exists.</summary>
    /// <remarks>
    /// A new draft starts from whatever is published, so editing a live form
    /// means changing it rather than rebuilding it. An application form with
    /// nothing published starts from <see cref="StartingQuestions"/> instead of
    /// from an empty page. Those are a starting point and not a rule: every one
    /// of them can be reworded or taken off from the first edit onwards.
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

    /// <summary>
    /// Sets who a form is for.
    /// </summary>
    /// <remarks>
    /// A property of the form rather than of a version, and deliberately not
    /// versioned with the questions. Who may answer is not something an
    /// applicant is shown and not something an answer is read against — it is
    /// the door, not the form — so freezing it into a published version would
    /// mean republishing a form to close it to a status, and nobody would.
    /// <para>
    /// Both halves at once, because they are one decision. Saving a gate
    /// without an audience is a form nobody can open, and a check constraint
    /// refuses that combination outright.
    /// </para>
    /// </remarks>
    /// <returns>The form as stored, or null when there is no such form.</returns>
    Task<Form?> SaveAudienceAsync(
        Guid formId,
        bool requiresSignIn,
        IReadOnlyList<string> eligibleStatuses,
        CancellationToken ct = default);
}
