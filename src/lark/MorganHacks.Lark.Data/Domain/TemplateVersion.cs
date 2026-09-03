namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// One saved version of a template, as the people who write them see it.
/// </summary>
/// <remarks>
/// Separate from <see cref="EmailTemplate"/>, which is the sending shape:
/// everything lark needs to put a message on the wire and nothing else. This
/// carries the Markdown source and the version number, which matter to an
/// editor and are dead weight in a send loop.
/// <para>
/// <see cref="Source"/> is nullable because the seeded <c>magic_link</c> row
/// was written as HTML in a migration and has no source. Saving it once through
/// the editor gives it one. <see cref="Format"/> says which language that
/// source is in; on a row with no source it is whatever the column defaulted
/// to and means nothing, which is what the API says about it.
/// </para>
/// <para>
/// <see cref="UpdatedAt"/> is the row's <c>created_at</c>, and that is not a
/// shortcut. Editing writes a new row rather than changing this one, so the
/// moment this version was written is the moment it was last changed — there is
/// no second timestamp to keep in step.
/// </para>
/// </remarks>
public sealed record TemplateVersion(
    Guid Id,
    string Key,
    string Kind,
    string Subject,
    string Format,
    string? Source,
    string Html,
    string Text,
    string FromLocal,
    string FromDomain,
    string? ReplyTo,
    int Version,
    DateTimeOffset UpdatedAt,
    Guid? CreatedBy)
{
    /// <summary>
    /// The same template in the shape the renderer understands.
    /// </summary>
    /// <remarks>
    /// So that the console's list of placeholders comes from
    /// <see cref="TemplateRenderer.PlaceholdersIn"/> — the same function
    /// <c>CampaignEndpoints</c> refuses a send on — rather than from a second
    /// regex that agrees with it until one of them is changed.
    /// </remarks>
    public EmailTemplate ForRendering() => new(
        Id, Key, Kind, Subject, Html, Text, FromLocal, FromDomain, ReplyTo);
}

/// <summary>What an author submits when they save a template.</summary>
/// <remarks>
/// <see cref="Html"/> and <see cref="Text"/> are both rendered from
/// <see cref="Source"/> by <see cref="TemplateBody.Render"/> before this record
/// is made. They are carried rather than re-derived in the store so that a
/// body which sanitises away to nothing is refused by the endpoint, with a
/// sentence, rather than by a NOT NULL column with an exception.
/// </remarks>
public sealed record TemplateDraft(
    string Key,
    string Kind,
    string Subject,
    string Format,
    string Source,
    string Html,
    string Text,
    string FromLocal,
    string FromDomain,
    string? ReplyTo);

/// <summary>How a save ended.</summary>
public enum TemplateWriteResult
{
    /// <summary>A new version exists.</summary>
    Written,

    /// <summary>Something already answers to that key.</summary>
    KeyTaken,

    /// <summary>Nothing answers to that key.</summary>
    NoSuchTemplate,

    /// <summary>The save would have changed the template's lane.</summary>
    KindChanged,

    /// <summary>Somebody else saved the same template first.</summary>
    Superseded,
}

/// <summary>
/// The outcome of a save.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> is the kind already on file, carried out so the refusal
/// can say what it is rather than only that it differs.
/// </remarks>
public sealed record TemplateWrite(
    TemplateWriteResult Result, TemplateVersion? Template = null, string? Kind = null);
