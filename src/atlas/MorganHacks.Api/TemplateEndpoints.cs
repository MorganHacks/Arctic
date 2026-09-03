using System.Globalization;
using System.Text.RegularExpressions;
using MorganHacks.Identity.Domain;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// The templates mail is written in, for the people who write them.
/// </summary>
/// <remarks>
/// One permission across the whole surface: <c>email.manage_templates</c>, on
/// reads as well as writes. Unlike campaigns, there is no useful narrower
/// reader here — a template is not a record of something that happened, it is
/// the thing that will be sent, and the only reason to look at one is to write
/// or check the wording. It is the same permission that drafts a campaign,
/// which is deliberate: choosing a template and writing one are the same job
/// done by the same people, and splitting them would mean somebody who can
/// pick a template cannot read what is in it.
/// <para>
/// <b>Authors write Markdown or HTML, and say which.</b> One source either
/// way: <c>body_html</c> and <c>body_text</c> are both generated from it and
/// neither is accepted from the caller — see <see cref="TemplateMarkdown"/>
/// for why one source rather than two, <see cref="TemplateBody"/> for what
/// each language renders through, and <see cref="EmailHtml"/> for what a
/// template is allowed to contain. The short version, because it gets asked:
/// tables, inline styles and the old presentational attributes are all
/// allowed, because that is what email is built out of; JavaScript is not, in
/// any spelling, because no client runs it and trying scores against the
/// message.
/// </para>
/// <para>
/// <b>Saving copies rather than overwrites.</b> A template a sent campaign
/// points at is a record of what this event mailed people, so editing writes a
/// new row and retires the old one. 0017 carries the argument. The consequence
/// visible from here is that a draft campaign approved against the old wording
/// stops being sendable the moment somebody edits the template, and
/// <see cref="CampaignEndpoints"/> already says so in the words it was given:
/// "This campaign's template has changed or been removed."
/// </para>
/// <para>
/// Nothing here logs a subject or a body. Keys, versions and person ids, so a
/// log that leaks says which template changed and never what it now says.
/// </para>
/// </remarks>
public static partial class TemplateEndpoints
{
    /// <summary>The longest subject line this will store.</summary>
    /// <remarks>
    /// Not an RFC limit — that is far higher. This is the point past which
    /// every mail client truncates, so a longer one is a subject whose ending
    /// nobody will read.
    /// </remarks>
    private const int MaxSubjectLength = 200;

    /// <summary>The longest Markdown body this will store.</summary>
    /// <remarks>
    /// A bound on an unbounded column reachable by an authenticated organizer,
    /// and generous: the longest email anybody has sent from here would be a
    /// twentieth of it.
    /// </remarks>
    private const int MaxMarkdownLength = 50_000;

    /// <summary>The longest HTML body this will store.</summary>
    /// <remarks>
    /// Four times the Markdown bound, because HTML says the same thing in far
    /// more characters: a nested layout table with a style attribute on every
    /// cell is most of an export from a builder, and holding HTML to a limit
    /// written for prose would refuse ordinary newsletters. Still a bound —
    /// this is an unbounded column behind an admin session.
    /// </remarks>
    private const int MaxHtmlLength = 200_000;

    private const int MaxKeyLength = 64;

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$")]
    private static partial Regex Key { get; }

    [GeneratedRegex(@"^[A-Za-z0-9._%+-]+$")]
    private static partial Regex LocalPart { get; }

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)+$")]
    private static partial Regex Domain { get; }

    public static IEndpointRouteBuilder MapTemplates(this IEndpointRouteBuilder app)
    {
        var templates = app.MapGroup("/admin/templates");

        templates.MapGet("", List)
                 .RequirePermission(Permission.EmailManageTemplates);

        // Ahead of the {key} routes it shares a prefix with. It is a POST and
        // they are GET and PUT, so nothing actually collides today — the order
        // is here so that adding a GET /{key} sibling later cannot quietly
        // shadow it.
        templates.MapPost("/preview", Preview)
                 .RequirePermission(Permission.EmailManageTemplates);

        // Ahead of GET /{key} for real this time: a literal segment beats a
        // route parameter in ASP.NET's matcher, so this would win wherever it
        // sat, but a reader should not have to know that to be sure there is
        // no template called "placeholders".
        templates.MapGet("/placeholders", Placeholders)
                 .RequirePermission(Permission.EmailManageTemplates);

        templates.MapGet("/{key}", One)
                 .RequirePermission(Permission.EmailManageTemplates);

        templates.MapPost("", Create)
                 .RequirePermission(Permission.EmailManageTemplates);
        templates.MapPut("/{key}", Revise)
                 .RequirePermission(Permission.EmailManageTemplates);

        return app;
    }

    /// <summary>
    /// The body <see cref="Create"/> and <see cref="Revise"/> take.
    /// </summary>
    /// <remarks>
    /// Nullable throughout for the reason CampaignEndpoints and PeopleEndpoints
    /// give: minimal APIs bind the body before endpoint filters run, so a
    /// required body answers a request with none before the permission gate has
    /// looked at it. Optional here and checked in the handler means
    /// authorization answers first.
    /// <para>
    /// <c>format</c> is <c>markdown</c> or <c>html</c> and says how to read
    /// <c>body</c>. Left out it is <c>markdown</c>, which is what every caller
    /// written before there was a choice meant, so an old request keeps
    /// working unchanged.
    /// </para>
    /// <para>
    /// <c>markdown</c> is the old name for <c>body</c> and is still accepted,
    /// for the same reason. Sending both is fine while they agree and is
    /// refused when they do not — two spellings of the source that differ is a
    /// caller with a bug, and guessing which one it meant to send is guessing
    /// about the thing that will be mailed to several hundred people.
    /// </para>
    /// <para>
    /// Still no <c>html</c> or <c>text</c> field, and there will not be one.
    /// Both columns are generated from the source, and a caller able to supply
    /// either is a caller able to make them disagree with each other and with
    /// what an author will be shown when they next open the editor.
    /// </para>
    /// </remarks>
    public sealed record TemplateRequest(
        string? Key,
        string? Kind,
        string? Subject,
        string? Format,
        string? Body,
        string? Markdown,
        string? FromLocal,
        string? FromDomain,
        string? ReplyTo);

    /// <summary>
    /// The body <see cref="Preview"/> takes.
    /// </summary>
    /// <remarks>
    /// <c>Values</c> is what the placeholders should be filled with. Left out,
    /// nothing is filled and the placeholders stand — which is
    /// <see cref="TemplateRenderer"/>'s behaviour for a value it does not hold,
    /// and the right thing to show an author: a visible <c>{{firstName}}</c> is
    /// how somebody notices they have asked for a field before they send it to
    /// four hundred people.
    /// </remarks>
    public sealed record PreviewRequest(
        string? Subject,
        string? Format,
        string? Body,
        string? Markdown,
        Dictionary<string, string>? Values);

    // ------------------------------------------------------------- reading ---

    /// <summary>
    /// Every template, by key. Requires <c>email.manage_templates</c>.
    /// </summary>
    /// <remarks>
    /// Live versions only, and no bodies. This is the screen somebody opens to
    /// pick which template to edit, and shipping every body to draw a list of
    /// eight rows is several hundred kilobytes to decide which link to click.
    /// </remarks>
    private static async Task<IResult> List(TemplateCatalog templates, CancellationToken ct)
    {
        var listed = await templates.ListAsync(ct);
        return Results.Ok(new { templates = listed.Select(Summary) });
    }

    /// <summary>
    /// One template, with what it needs filled in. Requires
    /// <c>email.manage_templates</c>.
    /// </summary>
    /// <remarks>
    /// <c>body</c> is the source to edit and <c>format</c> says which language
    /// it is in; <c>html</c> and <c>text</c> are what will actually be sent.
    /// All of them, because an editor that showed only the source could not
    /// tell an author that the paragraph they pasted lost its styling, and one
    /// that showed only the output would be the round trip through generated
    /// HTML this whole change exists to remove.
    /// <para>
    /// <c>body</c> is null for a template written before there was an editor —
    /// the seeded <c>magic_link</c> row is one. Saving it once through here
    /// gives it a source, and the console should say so rather than showing an
    /// empty box beside a body that is plainly not empty. On such a row
    /// <c>format</c> is the column's default and describes nothing.
    /// </para>
    /// </remarks>
    private static async Task<IResult> One(
        string key, TemplateCatalog templates, CancellationToken ct)
    {
        var template = await templates.FindAsync(key, ct);
        return template is null
            ? Results.NotFound(new { error = NoSuchTemplate })
            : Results.Ok(Detail(template));
    }

    /// <summary>
    /// Every <c>{{placeholder}}</c> that resolves. Requires
    /// <c>email.manage_templates</c>.
    /// </summary>
    /// <remarks>
    /// So the editor can offer them rather than leaving an author to guess and
    /// find out from a send that refuses. The list is
    /// <see cref="MergeFields"/>, which is the same list
    /// <see cref="CampaignEndpoints"/> renders a broadcast from — offering a
    /// fourth name here would be offering a template that gets written,
    /// reviewed, and then refused by the send.
    /// <para>
    /// Reads no template and takes no key, because a placeholder resolves the
    /// same way in every template. Narrowing to one campaign's segment is
    /// <see cref="CampaignEndpoints"/>' route, where the segment is.
    /// </para>
    /// </remarks>
    private static IResult Placeholders() => Results.Ok(new
    {
        placeholders = MergeFields.All
            .Select(field => new { name = field.Name, description = field.Description }),
    });

    // ------------------------------------------------------------- writing ---

    /// <summary>
    /// Writes a template that did not exist. Requires
    /// <c>email.manage_templates</c>.
    /// </summary>
    /// <remarks>
    /// The reason this endpoint exists at all: until it did, the only way to
    /// add a template was an <c>INSERT</c> typed by hand against production,
    /// so the only one that had ever been added was the sign-in link a
    /// migration seeded — and with no broadcast template in the table, no mass
    /// mail could be sent even though every other part of the campaign surface
    /// was finished.
    /// </remarks>
    private static async Task<IResult> Create(
        TemplateRequest? request,
        HttpContext http,
        TemplateCatalog templates,
        ILogger<TemplateRequest> log,
        CancellationToken ct)
    {
        var key = request?.Key?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return Results.BadRequest(new
            {
                error = "A template needs a key. It is the name the rest of the "
                        + "system finds it by, like magic_link.",
            });
        }

        if (key.Length > MaxKeyLength || !Key.IsMatch(key))
        {
            return Results.BadRequest(new { error = BadKey });
        }

        if (!TryDraft(request, key, out var draft, out var refusal))
        {
            return Results.BadRequest(new { error = refusal });
        }

        var written = await templates.CreateAsync(draft!, http.PersonId(), ct);
        if (written.Result == TemplateWriteResult.KeyTaken)
        {
            return Results.Conflict(new
            {
                error = "There is already a template with that key. Open it and "
                        + "edit it, or choose another key.",
            });
        }

        log.LogInformation(
            "A template was written. {actor} {template} {kind} {version} {event}",
            http.PersonId(), key, draft!.Kind, 1, Events.TemplateWritten);

        return Results.Created($"/admin/templates/{key}", Detail(written.Template!));
    }

    /// <summary>
    /// Saves a new version of a template. Requires
    /// <c>email.manage_templates</c>.
    /// </summary>
    /// <remarks>
    /// The old version is retired rather than overwritten, so this answers with
    /// a higher <c>version</c> and the row a sent campaign points at is
    /// untouched. What that costs is the one thing worth saying on the screen:
    /// a campaign somebody has already drafted against this template can no
    /// longer be sent, and has to be drafted again. That is the intended price
    /// — an approver signs off on a template and a segment together, and a
    /// broadcast cannot be recalled — but it is a surprise if nobody is told.
    /// <para>
    /// <c>kind</c> has to match what is on file. See <see cref="KindIsFixed"/>
    /// for what changing it would do; the trigger 0017 installs is what makes
    /// the rule true of the table rather than true of this handler.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Revise(
        string key,
        TemplateRequest? request,
        HttpContext http,
        TemplateCatalog templates,
        ILogger<TemplateRequest> log,
        CancellationToken ct)
    {
        var named = request?.Key?.Trim();
        if (!string.IsNullOrEmpty(named) && named != key)
        {
            return Results.BadRequest(new
            {
                error = "A template's key cannot be changed. Create a new template "
                        + "with the key you want.",
            });
        }

        if (!TryDraft(request, key, out var draft, out var refusal))
        {
            return Results.BadRequest(new { error = refusal });
        }

        var written = await templates.ReviseAsync(key, draft!, http.PersonId(), ct);

        switch (written.Result)
        {
            case TemplateWriteResult.NoSuchTemplate:
                return Results.NotFound(new { error = NoSuchTemplate });

            case TemplateWriteResult.KindChanged:
                return Results.Conflict(new { error = KindIsFixed(written.Kind!) });

            case TemplateWriteResult.Superseded:
                // 409 rather than 400: nothing about the request is malformed,
                // it was written against a version that is no longer the
                // current one. Retrying it blindly would overwrite whatever the
                // other person just saved.
                return Results.Conflict(new
                {
                    error = "Somebody else saved this template while you were "
                            + "editing it. Reload it and make your change again.",
                });

            default:
                log.LogInformation(
                    "A template was written. {actor} {template} {kind} {version} {event}",
                    http.PersonId(), key, draft!.Kind, written.Template!.Version,
                    Events.TemplateWritten);

                return Results.Ok(Detail(written.Template));
        }
    }

    // ----------------------------------------------------------- previewing ---

    /// <summary>
    /// Renders an unsaved draft. Requires <c>email.manage_templates</c>.
    /// </summary>
    /// <remarks>
    /// Takes the draft in the body rather than reading a stored template,
    /// because the editor calls this while somebody is typing and there is
    /// nothing saved to read. Nothing is written and no template has to exist.
    /// <para>
    /// It renders through exactly the path a save would — the same Markdown
    /// dialect, the same allow-list, the same <see cref="TemplateRenderer"/>
    /// that fills placeholders at queue time. A preview that agreed with the
    /// editor and disagreed with the send would be worse than no preview,
    /// because it would be believed.
    /// </para>
    /// </remarks>
    private static IResult Preview(PreviewRequest? request)
    {
        var subject = request?.Subject ?? string.Empty;

        if (!TryFormat(request?.Format, out var format, out var refusal))
        {
            return Results.BadRequest(new { error = refusal });
        }

        if (!TrySource(request?.Body, request?.Markdown, out var source, out refusal))
        {
            return Results.BadRequest(new { error = refusal });
        }

        source ??= string.Empty;

        if (source.Length > Longest(format))
        {
            return Results.BadRequest(new { error = BodyTooLong(format) });
        }

        if (subject.Length > MaxSubjectLength)
        {
            return Results.BadRequest(new { error = SubjectTooLong });
        }

        // Deliberately not refused when empty. This is called on a keystroke,
        // and an editor that answers 400 until somebody has finished typing is
        // an editor that flashes an error at them the whole time they work.
        var rendered = TemplateRenderer.Render(
            Draft(subject, format, source),
            request?.Values ?? new Dictionary<string, string>(StringComparer.Ordinal));

        return Results.Ok(new
        {
            subject = rendered.Subject,
            format,
            html = rendered.BodyHtml,
            text = rendered.BodyText,
        });
    }

    /// <summary>A template that is never stored, for rendering something unsaved.</summary>
    /// <remarks>
    /// The addresses are placeholders and the kind is a lie, because neither is
    /// read: <see cref="TemplateRenderer.Render"/> touches the subject and the
    /// two bodies and nothing else. Made here rather than by loosening the
    /// renderer to take three strings, so that the one function that fills
    /// placeholders keeps taking the one type that has them.
    /// </remarks>
    private static EmailTemplate Draft(string subject, string format, string source)
    {
        var (html, text) = TemplateBody.Render(format, source);

        return new EmailTemplate(
            Guid.Empty,
            "preview",
            "broadcast",
            subject,
            html,
            text,
            "preview",
            "invalid",
            null);
    }

    // ------------------------------------------------------------- checking ---

    /// <summary>
    /// Turns a request into something storable, or into a sentence saying why
    /// not.
    /// </summary>
    /// <remarks>
    /// Shared by create and save, because a template that could be created and
    /// not re-saved — or the other way round — would be a template somebody can
    /// get stuck inside.
    /// </remarks>
    private static bool TryDraft(
        TemplateRequest? request, string key, out TemplateDraft? draft, out string? refusal)
    {
        draft = null;
        refusal = null;

        var kind = request?.Kind?.Trim();
        if (kind is not ("transactional" or "broadcast"))
        {
            refusal = "A template's kind has to be either transactional or broadcast. "
                      + "Transactional is mail somebody asked for by doing something, "
                      + "like a sign-in link; broadcast is mail we decided to send.";
            return false;
        }

        var subject = request?.Subject?.Trim();
        if (string.IsNullOrEmpty(subject))
        {
            refusal = "A template needs a subject line.";
            return false;
        }

        if (subject.Length > MaxSubjectLength)
        {
            refusal = SubjectTooLong;
            return false;
        }

        if (!TryFormat(request?.Format, out var format, out refusal))
        {
            return false;
        }

        if (!TrySource(request?.Body, request?.Markdown, out var source, out refusal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            refusal = NeedsABody(format);
            return false;
        }

        if (source.Length > Longest(format))
        {
            refusal = BodyTooLong(format);
            return false;
        }

        var fromLocal = request?.FromLocal?.Trim();
        var fromDomain = request?.FromDomain?.Trim();
        if (string.IsNullOrEmpty(fromLocal) || string.IsNullOrEmpty(fromDomain))
        {
            refusal = "A template needs an address to send from: a local part and a "
                      + "domain, like news and news.morganhacks.com.";
            return false;
        }

        if (!IsAddress(fromLocal, fromDomain))
        {
            refusal = "That is not an address mail can be sent from. Check the local "
                      + "part and the domain.";
            return false;
        }

        var replyTo = request?.ReplyTo?.Trim();
        if (replyTo?.Length == 0)
        {
            replyTo = null;
        }

        if (replyTo is not null && !IsAddress(replyTo))
        {
            refusal = "That reply-to is not a valid email address. Leave it empty if "
                      + "replies should go to the from address.";
            return false;
        }

        // Both bodies, from the one source, through the allow-list. Done here
        // rather than in the store so that a body which survives sanitisation
        // as nothing is refused before a NOT NULL column is asked to hold an
        // empty string.
        var (html, text) = TemplateBody.Render(format, source);

        if (html.Length == 0 || text.Length == 0)
        {
            refusal = "That body renders to nothing an email can carry. Scripts, "
                      + "iframes and event handlers are removed, because no mail "
                      + "client runs them — check that what you wrote is not only "
                      + "those.";
            return false;
        }

        draft = new TemplateDraft(
            key, kind, subject, format, source, html, text, fromLocal, fromDomain, replyTo);
        return true;
    }

    /// <summary>
    /// Which language the source is in, or a sentence saying why that is not a
    /// language.
    /// </summary>
    /// <remarks>
    /// Absent means Markdown, which is what every caller written before there
    /// was a choice meant. A word that is neither is refused rather than
    /// treated as the default: a caller that sent <c>"HTML "</c> and got a
    /// Markdown render back would find out from the mail.
    /// </remarks>
    private static bool TryFormat(string? asked, out string format, out string? refusal)
    {
        format = asked?.Trim().ToLowerInvariant() is { Length: > 0 } named
            ? named
            : TemplateBody.Markdown;

        refusal = TemplateBody.IsFormat(format) ? null : BadFormat;
        return refusal is null;
    }

    /// <summary>
    /// The source, under whichever of its two names the caller used.
    /// </summary>
    /// <remarks>
    /// <c>markdown</c> is the older name and is kept working. Both names
    /// carrying different text is refused rather than resolved, because either
    /// resolution is a guess about what gets mailed.
    /// </remarks>
    private static bool TrySource(
        string? body, string? markdown, out string? source, out string? refusal)
    {
        refusal = null;

        if (body is not null && markdown is not null && body != markdown)
        {
            source = null;
            refusal = "A template has one body. It was sent twice here, as body and "
                      + "as markdown, saying two different things. Send one of them.";
            return false;
        }

        source = body ?? markdown;
        return true;
    }

    private static int Longest(string format) =>
        format == TemplateBody.Html ? MaxHtmlLength : MaxMarkdownLength;

    private static bool IsAddress(string address)
    {
        var at = address.LastIndexOf('@');
        return at > 0 && at < address.Length - 1
            && IsAddress(address[..at], address[(at + 1)..]);
    }

    private static bool IsAddress(string local, string domain) =>
        local.Length <= 64
        && domain.Length <= 255
        && LocalPart.IsMatch(local)
        && Domain.IsMatch(domain);

    // -------------------------------------------------------------- shaping ---

    private const string NoSuchTemplate = "There is no template with that key.";

    private const string BadKey =
        "A template key can only contain lowercase letters, numbers, underscores "
        + "and hyphens, has to start with a letter or a number, and has to be 64 "
        + "characters or fewer.";

    private static readonly string SubjectTooLong = string.Format(
        CultureInfo.InvariantCulture,
        "That subject line is longer than {0:N0} characters, which every mail "
        + "client cuts off. Shorten it.",
        MaxSubjectLength);

    private const string BadFormat =
        "A template's body is written either in Markdown or in HTML, so format "
        + "has to be markdown or html.";

    /// <summary>
    /// Names the limit that was actually applied.
    /// </summary>
    /// <remarks>
    /// The two are far apart, and a refusal quoting the wrong one sends
    /// somebody off to cut an HTML body down to a length it never had to meet.
    /// </remarks>
    private static string BodyTooLong(string format) => string.Format(
        CultureInfo.InvariantCulture,
        "That template body is longer than {0:N0} characters, which is longer "
        + "than an email should be.",
        Longest(format));

    private static string NeedsABody(string format) => format == TemplateBody.Html
        ? "A template needs a body. Write it in HTML."
        : "A template needs a body. Write it in Markdown.";

    /// <summary>
    /// Why the kind on a template is the kind it keeps.
    /// </summary>
    /// <remarks>
    /// The refusal names the consequence rather than the rule, because the rule
    /// on its own reads like bureaucracy and the consequence does not.
    /// <c>kind</c> decides the queue lane and the sending subdomain: a
    /// transactional template turned broadcast would put every sign-in link
    /// behind whatever announcement is draining and send it from the domain
    /// that collects the spam complaints, which is the failure the two lanes
    /// exist to prevent.
    /// </remarks>
    private static string KindIsFixed(string settled) => string.Format(
        CultureInfo.InvariantCulture,
        "This template is {0} and cannot be changed to something else. The kind "
        + "decides which queue a message joins and which subdomain it sends "
        + "from, and campaigns and sign-in links already point at this one. "
        + "Create a new template instead.",
        settled);

    private static object Summary(TemplateVersion template) => new
    {
        key = template.Key,
        kind = template.Kind,
        subject = template.Subject,
        format = template.Format,
        version = template.Version,
        updatedAt = template.UpdatedAt,
    };

    /// <summary>
    /// One template, in the shape an editor opens.
    /// </summary>
    /// <remarks>
    /// <c>body</c> is the source and <c>format</c> says how to read it.
    /// <c>markdown</c> is the same string under its older name, and is null on
    /// an HTML template rather than holding HTML — a caller that has not been
    /// taught about formats yet should find nothing there rather than find
    /// markup it will show in a Markdown editor.
    /// </remarks>
    private static object Detail(TemplateVersion template) => new
    {
        key = template.Key,
        kind = template.Kind,
        subject = template.Subject,
        format = template.Format,
        body = template.Source,
        markdown = template.Format == TemplateBody.Html ? null : template.Source,
        html = template.Html,
        text = template.Text,
        fromLocal = template.FromLocal,
        fromDomain = template.FromDomain,
        replyTo = template.ReplyTo,
        version = template.Version,

        // From TemplateRenderer, which is the same function CampaignEndpoints
        // refuses a send on. A second regex here would agree with it right up
        // until one of the two was changed, and the disagreement would surface
        // as a campaign the console said was fine and the API would not send.
        placeholders = TemplateRenderer.PlaceholdersIn(template.ForRendering())
            .OrderBy(placeholder => placeholder, StringComparer.Ordinal),
    };
}
