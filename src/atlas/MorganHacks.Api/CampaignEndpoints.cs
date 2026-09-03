using System.Globalization;
using System.Text.Json;
using MorganHacks.Applications.Segments;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// Mass mail, for the people who send it.
/// </summary>
/// <remarks>
/// Four permissions' worth of surface, split along the thing that actually
/// differs: who can write one down, who can read what happened, and who can
/// make it leave the building.
/// <list type="bullet">
/// <item><c>email.view_stats</c> lists campaigns and reads one back. Anybody
/// who is asked "did that go out" needs this and needs nothing else.</item>
/// <item><c>email.manage_templates</c> drafts a campaign and previews it.
/// Previewing is part of drafting — somebody who cannot check who their own
/// segment resolves to cannot be expected to hand a correct one to an
/// approver — and it reveals nothing the same people do not already read
/// through <c>applications.view</c>, which comms holds for exactly this
/// purpose.</item>
/// <item><c>email.send_broadcast</c> queues it and stops it. On the sensitive
/// list, and the only permission here that causes an email to exist.</item>
/// </list>
/// <para>
/// <b>Two people, not one.</b> The <c>approved_by</c> column in 0003 is
/// commented "Broadcasts only. Transactional sends have no approver", and the
/// reading taken here is the strong one: <see cref="Send"/> refuses when the
/// person pressing it is the person who drafted it. A campaign therefore has
/// two names on it, and they are different names.
/// </para>
/// <para>
/// The reason is that every other mistake in this system is recoverable and
/// this one is not. A wrong status can be set back, a wrong export can be
/// deleted, a wrong form can be republished — four hundred people who have
/// received the wrong email have received it. The person best placed to miss a
/// mistake in a segment or a template is the person who just built it, and a
/// second pair of eyes costs one message in a group chat. There is no override
/// flag, because an override flag is the rule not existing: both comms and
/// super-admin hold <c>email.send_broadcast</c> by baseline, so a second
/// holder always exists.
/// </para>
/// <para>
/// <b>Pressing send twice sends once.</b> Enforced in
/// <see cref="CampaignStore.QueueAsync"/> by a conditional transition out of
/// <c>draft</c> inside the same transaction that writes the messages, with the
/// unique indexes on <c>notify.messages</c> underneath it. Nothing about that
/// guarantee lives in this file — a check here would be a check outside the
/// transaction, which is a race with a comment on it.
/// </para>
/// <para>
/// Nothing here logs an address. Campaign ids, actor ids and counts, so a log
/// that leaks says what went out and never who received it — except in the
/// preview response, which is the one place an organizer is deliberately shown
/// a handful of addresses, and which is not logged.
/// </para>
/// </remarks>
public static class CampaignEndpoints
{
    /// <summary>How many addresses a preview shows.</summary>
    /// <remarks>
    /// Enough to recognise the segment, nowhere near enough to be a copy of
    /// it. Somebody who wants the list wants <c>applications.export</c>, which
    /// is on the sensitive list precisely because taking one leaves the
    /// system.
    /// </remarks>
    private const int SampleSize = 10;

    /// <summary>The longest a campaign's name may be.</summary>
    /// <remarks>
    /// Internal-only text — it never reaches a recipient — so this is a bound
    /// on a text column rather than a style rule.
    /// </remarks>
    private const int MaxNameLength = 200;

    public static IEndpointRouteBuilder MapCampaigns(this IEndpointRouteBuilder app)
    {
        var campaigns = app.MapGroup("/admin/campaigns");

        campaigns.MapGet("", List)
                 .RequirePermission(Permission.EmailViewStats);
        campaigns.MapGet("/{id:guid}", One)
                 .RequirePermission(Permission.EmailViewStats);

        campaigns.MapPost("", Create)
                 .RequirePermission(Permission.EmailManageTemplates);
        campaigns.MapPost("/{id:guid}/preview", Preview)
                 .RequirePermission(Permission.EmailManageTemplates);

        // The two that move mail. Same permission for both, and deliberately:
        // stopping a broadcast must never be harder to reach than starting
        // one, because the moment it is needed is the moment somebody has
        // already made a mistake.
        campaigns.MapPost("/{id:guid}/send", Send)
                 .RequirePermission(Permission.EmailSendBroadcast);
        campaigns.MapPost("/{id:guid}/cancel", Cancel)
                 .RequirePermission(Permission.EmailSendBroadcast);

        return app;
    }

    /// <summary>
    /// The body <see cref="Create"/> takes.
    /// </summary>
    /// <remarks>
    /// Nullable throughout for the reason PeopleEndpoints and AdminFormEndpoints
    /// give: minimal APIs bind the body before endpoint filters run, so a
    /// required body answers a request with none before the permission gate has
    /// looked at it. Optional here and checked in the handler means
    /// authorization answers first.
    /// <para>
    /// <c>Segment</c> is a raw <see cref="JsonElement"/> rather than a bound
    /// type. It is a tagged union whose stored form is the contract — see
    /// <see cref="Segment"/> — and letting a serializer guess at it would mean
    /// the shape on the row was decided by binding rules rather than by
    /// something that can refuse.
    /// </para>
    /// </remarks>
    public sealed record CreateCampaignRequest(
        string? Name, string? TemplateKey, JsonElement? Segment);

    // ------------------------------------------------------------- reading ---

    /// <summary>
    /// Campaigns newest first. Requires <c>email.view_stats</c>.
    /// </summary>
    /// <remarks>
    /// Broadcasts only. Every sign-in link writes a campaign row of its own —
    /// see <see cref="MessageQueue.EnqueueTransactionalAsync"/> for why — so by
    /// the end of registration week this table is overwhelmingly login links,
    /// and the store filters them out on <c>created_by</c>. A magic link has no
    /// author; a broadcast always has one, and now has two.
    /// <para>
    /// No message counts. This is a list of forty rows and counting each one's
    /// messages is forty grouped scans to draw a screen whose job is to let
    /// somebody click the right campaign. <see cref="One"/> is where progress
    /// is counted.
    /// </para>
    /// </remarks>
    private static async Task<IResult> List(CampaignStore campaigns, CancellationToken ct)
    {
        var listed = await campaigns.ListAsync(ct: ct);
        return Results.Ok(new { campaigns = listed.Select(Describe) });
    }

    /// <summary>
    /// One campaign, with what actually happened to its messages. Requires
    /// <c>email.view_stats</c>, and <c>email.manage_templates</c> for the
    /// sample.
    /// </summary>
    /// <remarks>
    /// <c>recipientCount</c> is the frozen number: how many people this was
    /// queued to, which for a draft is zero because a draft has been queued to
    /// nobody. It is deliberately not "how many the segment would match right
    /// now" — that number changes every hour of registration week, and a screen
    /// that showed it beside a sent campaign would be quietly reporting a
    /// different send than the one that happened. <see cref="Preview"/> is the
    /// endpoint that answers the live question, and it says so by being a POST.
    /// <para>
    /// The addresses are behind a second permission, checked in the handler
    /// the way <see cref="FormResponseEndpoints"/> checks for a resume link.
    /// <c>email.view_stats</c> is the permission for "did that go out", and
    /// the numbers answer that on their own; reading who somebody is belongs
    /// with drafting, which is where <see cref="Preview"/> already shows
    /// addresses. Comms holds both by baseline, so this costs the intended
    /// reader nothing and closes the case where <c>email.view_stats</c> is
    /// granted to somebody on its own.
    /// </para>
    /// </remarks>
    private static async Task<IResult> One(
        Guid id,
        HttpContext http,
        CampaignStore campaigns,
        PermissionService permissions,
        CancellationToken ct)
    {
        var campaign = await campaigns.FindAsync(id, ct);
        if (campaign is null)
        {
            return Results.NotFound(new { error = "No such campaign." });
        }

        var progress = await campaigns.ProgressAsync(id, ct);

        var effective = await permissions.ForAsync(http.PersonId(), ct);

        // The frozen list, a corner of it. Present here and not only on the
        // preview because after a send this is the only place the question
        // "who did we actually mail" has an answer at all — the segment
        // resolves to somebody else by then.
        var sample = effective.Can(Permission.EmailManageTemplates)
            ? await campaigns.SampleAsync(id, SampleSize, ct)
            : null;

        return Results.Ok(new
        {
            campaign = Describe(campaign),
            messages = Describe(progress),
            sample,
        });
    }

    // ------------------------------------------------------------ drafting ---

    /// <summary>
    /// Writes down the intent. Requires <c>email.manage_templates</c>. Mails
    /// nobody.
    /// </summary>
    /// <remarks>
    /// The template must be a broadcast one, and the refusal is not
    /// bureaucratic. <c>kind</c> drives the lane and the sending subdomain, and
    /// a campaign pointed at a transactional template would put several
    /// hundred announcements into the queue at priority 0 — every one of them
    /// ahead of the sign-in links behind them, sent from the subdomain whose
    /// reputation exists so that login mail always arrives. That is the
    /// failure the whole two-lane design is built to prevent, reached by
    /// picking the wrong item in a dropdown.
    /// <para>
    /// The template is also checked here for placeholders the chosen segment
    /// cannot fill. It is checked again at send, because a template is data
    /// and can be edited in between; refusing here is so that the mistake is
    /// found by the person who can still fix it rather than by the approver.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Create(
        CreateCampaignRequest? request,
        HttpContext http,
        TemplateStore templates,
        CampaignStore campaigns,
        ILogger<CreateCampaignRequest> log,
        CancellationToken ct)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Results.BadRequest(new { error = "A campaign needs a name." });
        }

        if (name.Length > MaxNameLength)
        {
            return Results.BadRequest(new
            {
                error = "That campaign name is too long to store.",
            });
        }

        var key = request?.TemplateKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return Results.BadRequest(new { error = "Choose the template to send." });
        }

        var template = await templates.FindAsync(key, ct);
        if (template is null)
        {
            return Results.BadRequest(new { error = "There is no template with that key." });
        }

        if (template.IsTransactional)
        {
            return Results.BadRequest(new
            {
                error = "That template is for transactional mail, which cannot be "
                        + "broadcast. Choose a broadcast template.",
            });
        }

        if (request?.Segment is not { } json)
        {
            return Results.BadRequest(new { error = "A campaign needs a segment to send to." });
        }

        if (!Segment.TryParse(json, out var segment, out var wrong))
        {
            return Results.BadRequest(new { error = wrong });
        }

        var unfillable = Unfillable(template, segment!);
        if (unfillable is not null)
        {
            return Results.BadRequest(new { error = unfillable });
        }

        var campaign = await campaigns.CreateDraftAsync(
            template.Id, name, segment!.ToJson(), EventOf(segment), http.PersonId(), ct);

        log.LogInformation(
            "A broadcast was drafted. {actor} {campaign} {segment} {event}",
            http.PersonId(), campaign.Id, segment.Type, Events.CampaignCreated);

        return Results.Created($"/admin/campaigns/{campaign.Id}", Describe(campaign));
    }

    /// <summary>
    /// Resolves the segment now and says who that is. Requires
    /// <c>email.manage_templates</c>.
    /// </summary>
    /// <remarks>
    /// Nothing is written and nothing is frozen — this is the answer to "who
    /// does that mean today", asked before anybody commits to it. Running it
    /// twice can legitimately give two different numbers, which is the reason
    /// the send freezes its own list rather than trusting this one.
    /// <para>
    /// Suppressions are applied here as well as in lark's claim query, so the
    /// number on the confirmation screen is the number of people who will
    /// actually receive the mail rather than the size of the segment. On this
    /// lane every reason blocks: a bounce or a complaint because the address
    /// is dead either way, an unsubscribe because this is precisely the kind
    /// of mail somebody unsubscribes from. The mirror of that rule — an
    /// unsubscribe never standing between somebody and their sign-in link —
    /// is <see cref="MessageQueue.IsSuppressedAsync"/>'s and is tested.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Preview(
        Guid id,
        CampaignStore campaigns,
        TemplateStore templates,
        ISegmentResolver resolver,
        CancellationToken ct)
    {
        var campaign = await campaigns.FindAsync(id, ct);
        if (campaign is null)
        {
            return Results.NotFound(new { error = "No such campaign." });
        }

        if (!TryStored(campaign, out var segment, out var unreadable))
        {
            return unreadable;
        }

        var resolved = await resolver.ResolveAsync(segment!, ct);
        if (resolved.Overflowed)
        {
            return Results.BadRequest(new { error = TooMany });
        }

        var suppressed = await campaigns.SuppressedAmongAsync(
            resolved.Members.Select(m => m.Email).ToArray(), ct);

        var sendable = resolved.Members.Where(m => !suppressed.ContainsKey(m.Email)).ToList();

        // Advisory here and fatal at send. The person reading this screen is
        // the one who can still fix a template that greets people by a name
        // the segment does not carry; the approver behind them cannot.
        var template = await templates.FindAsync(campaign.TemplateKey, ct);
        var problems = new List<string>();

        if (template is null || template.Id != campaign.TemplateId)
        {
            // Reported here rather than only discovered at send, because this
            // is the screen where there is still time to do something about
            // it. Same condition Send refuses on, said earlier.
            problems.Add(MissingTemplate);
        }
        else if (Unfillable(template, segment!) is { } problem)
        {
            problems.Add(problem);
        }

        if (Missing(template, sendable) is { } gap)
        {
            problems.Add(gap);
        }

        return Results.Ok(new
        {
            campaignId = campaign.Id,
            segmentSize = resolved.Members.Count,

            // The number that matters: people who will receive something.
            recipientCount = sendable.Count,
            suppressedCount = suppressed.Count,
            suppressedByReason = suppressed.Values
                .GroupBy(r => r, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),

            // Of the ones who would be mailed, not of the segment. A sample
            // that quietly included somebody who is suppressed would be a
            // sample of the wrong list.
            sample = sendable.Take(SampleSize).Select(m => m.Email),
            problems,
        });
    }

    // -------------------------------------------------------------- sending ---

    /// <summary>
    /// Freezes the recipient list and queues it. Requires
    /// <c>email.send_broadcast</c>, and a different person from the one who
    /// drafted it.
    /// </summary>
    /// <remarks>
    /// Every refusal below happens before anything is written, and the one
    /// that is not a refusal happens inside a single transaction that either
    /// queues the whole campaign or none of it. Pressing this twice queues it
    /// once — see <see cref="CampaignStore.QueueAsync"/>, where that is a
    /// property of the database rather than of this handler.
    /// <para>
    /// Rendering happens here, once per recipient, and the result is stored on
    /// the row. That is 0003's rule and it is worth restating: a retry must
    /// not render differently from the attempt it is retrying, and an email
    /// approved on Tuesday should say on Thursday what it said when it was
    /// approved.
    /// </para>
    /// <para>
    /// The volume is not paced here and does not need to be. Ten thousand rows
    /// is one insert; lark's send loop claims twenty-five at a time and waits
    /// 140ms between sends, which is a hair under a 14/second SES quota with a
    /// second replica running — so the queue is the thing that absorbs the
    /// burst, which is what it is for. What this must not do is put those rows
    /// in front of a sign-in link, and it does not: they go in at priority 10,
    /// written as a constant rather than derived, and lark orders its claim by
    /// priority ascending.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Send(
        Guid id,
        HttpContext http,
        CampaignStore campaigns,
        TemplateStore templates,
        ISegmentResolver resolver,
        ILogger<Campaign> log,
        CancellationToken ct)
    {
        var campaign = await campaigns.FindAsync(id, ct);
        if (campaign is null)
        {
            return Results.NotFound(new { error = "No such campaign." });
        }

        if (!campaign.IsDraft)
        {
            // 409 rather than 400: nothing about the request is malformed, the
            // campaign is simply past the point where sending it means
            // anything. The status is handed back so the screen can say which
            // of "already sent" and "cancelled" happened.
            return Results.Conflict(new
            {
                error = "This campaign has already been sent or cancelled.",
                status = campaign.Status,
            });
        }

        var actor = http.PersonId();
        if (campaign.CreatedBy == actor)
        {
            // 403, and the copy says what to do rather than only what is
            // wrong. Somebody hitting this is not doing anything suspicious —
            // they are one step from sending correctly and need to know the
            // step is another person.
            return Results.Json(
                new
                {
                    error = "A broadcast has to be sent by somebody other than the "
                            + "person who wrote it. Ask another organizer with "
                            + "broadcast permission to send this one.",
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var (paused, reason) = await campaigns.BroadcastPauseAsync(ct);
        if (paused)
        {
            return Results.Conflict(new
            {
                error = "Broadcast sending is paused, so this was not queued.",
                pausedReason = reason,
            });
        }

        var template = await templates.FindAsync(campaign.TemplateKey, ct);

        // Identity as well as existence. The key is unique and the campaign
        // holds the id, so a key that resolves to a different template means
        // the row was rebuilt underneath this campaign — and sending whatever
        // now answers to that name is not what anybody approved.
        if (template is null || template.Id != campaign.TemplateId)
        {
            return Results.Conflict(new { error = MissingTemplate });
        }

        if (template.IsTransactional)
        {
            return Results.Conflict(new
            {
                error = "That template is for transactional mail, which cannot be "
                        + "broadcast. Choose a broadcast template.",
            });
        }

        if (!TryStored(campaign, out var segment, out var unreadable))
        {
            return unreadable;
        }

        var resolved = await resolver.ResolveAsync(segment!, ct);
        if (resolved.Overflowed)
        {
            return Results.BadRequest(new { error = TooMany });
        }

        if (resolved.Members.Count == 0)
        {
            // Refused rather than queued as nothing. A campaign that resolves
            // to nobody is almost always the wrong event or a status nobody is
            // in, and marking it 'sent' would file that mistake as a success.
            return Results.BadRequest(new
            {
                error = "This segment matches nobody right now, so there is "
                        + "nothing to send.",
            });
        }

        if (Unfillable(template, segment!) is { } unfillable)
        {
            return Results.BadRequest(new { error = unfillable });
        }

        var suppressed = await campaigns.SuppressedAmongAsync(
            resolved.Members.Select(m => m.Email).ToArray(), ct);

        var sendable = resolved.Members.Where(m => !suppressed.ContainsKey(m.Email)).ToList();

        if (Missing(template, sendable) is { } gap)
        {
            return Results.BadRequest(new { error = gap });
        }

        var recipients = resolved.Members.Select(member =>
        {
            var rendered = TemplateRenderer.Render(template, Values(member));
            return new BroadcastRecipient(
                member.PersonId, member.Email,
                rendered.Subject, rendered.BodyHtml, rendered.BodyText,
                Suppressed: suppressed.ContainsKey(member.Email));
        }).ToList();

        var outcome = await campaigns.QueueAsync(id, actor, recipients, ct);

        switch (outcome.Result)
        {
            case QueueResult.NoSuchCampaign:
                return Results.NotFound(new { error = "No such campaign." });

            case QueueResult.AlreadyLeftDraft:
                // Somebody — possibly a second click on the same button —
                // sent it between the read at the top and the transaction.
                // Not an error the caller can act on, and not a reason to try
                // again: it went out once, which is what was asked.
                return Results.Conflict(new
                {
                    error = "This campaign has already been sent or cancelled.",
                });

            default:
                log.LogInformation(
                    "A broadcast was queued. {actor} {author} {campaign} {recipients} "
                    + "{suppressed} {event}",
                    actor, campaign.CreatedBy, id, outcome.Queued, outcome.Suppressed,
                    Events.CampaignQueued);

                return Results.Ok(new
                {
                    campaignId = id,
                    status = "queued",
                    queued = outcome.Queued,
                    suppressed = outcome.Suppressed,

                    // Handed back so the screen can say it, and so a test can
                    // assert it without reading the table.
                    priority = 10,
                });
        }
    }

    /// <summary>
    /// Stops what has not gone yet. Requires <c>email.send_broadcast</c>.
    /// </summary>
    /// <remarks>
    /// No second person. Two-person control exists to stop mail going out by
    /// mistake, and applying it to the brake as well as the accelerator would
    /// mean a mistake in progress kept sending while somebody looked for a
    /// colleague.
    /// <para>
    /// It stops <c>pending</c> rows and nothing else. Anything a worker has
    /// already claimed is between us and SES, and the response says how many
    /// that was rather than implying they were recalled — a partly-sent
    /// campaign has to read as partly sent, because the next question is
    /// always "who got it".
    /// </para>
    /// </remarks>
    private static async Task<IResult> Cancel(
        Guid id,
        HttpContext http,
        CampaignStore campaigns,
        ILogger<Campaign> log,
        CancellationToken ct)
    {
        var outcome = await campaigns.CancelAsync(id, ct);

        switch (outcome.Result)
        {
            case CancelResult.NoSuchCampaign:
                return Results.NotFound(new { error = "No such campaign." });

            case CancelResult.NothingToStop:
                return Results.Conflict(new
                {
                    error = "This campaign has already finished, so there is "
                            + "nothing left to stop.",
                });

            default:
                log.LogInformation(
                    "A broadcast was stopped. {actor} {campaign} {stopped} {gone} {event}",
                    http.PersonId(), id, outcome.Stopped, outcome.AlreadyGone,
                    Events.CampaignCancelled);

                return Results.Ok(new
                {
                    campaignId = id,
                    status = "cancelled",
                    stopped = outcome.Stopped,

                    // Named plainly. "Cancelled" beside a number of messages
                    // that are already at the provider is the one thing about
                    // this response somebody must not misread.
                    alreadySent = outcome.AlreadyGone,
                });
        }
    }

    // ------------------------------------------------------------- checking ---

    /// <summary>
    /// The merge values a segment can supply for one recipient.
    /// </summary>
    /// <remarks>
    /// Three, and no more. Every value here is one a template author can rely
    /// on for every recipient of every segment, which is the property that
    /// makes <see cref="Unfillable"/> able to refuse before the send rather
    /// than after it. Growing this list is cheap; growing it by something only
    /// some segments carry is how "Hi {{school}}," reaches four hundred people.
    /// </remarks>
    private static Dictionary<string, string> Values(SegmentMember member)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["email"] = member.Email,
        };

        if (!string.IsNullOrWhiteSpace(member.FirstName))
        {
            values["firstName"] = member.FirstName;
        }

        if (!string.IsNullOrWhiteSpace(member.LastName))
        {
            values["lastName"] = member.LastName;
        }

        return values;
    }

    /// <summary>
    /// The placeholders a segment can fill for everybody in it.
    /// </summary>
    /// <remarks>
    /// A typed list of addresses carries an address and nothing else — the
    /// recipient is frequently a sponsor contact this system has never heard
    /// of — so a template that greets people by name cannot be sent to one.
    /// </remarks>
    private static IReadOnlySet<string> Fillable(Segment segment) => segment switch
    {
        Segment.Addresses => new HashSet<string>(StringComparer.Ordinal) { "email" },
        _ => new HashSet<string>(StringComparer.Ordinal) { "email", "firstName", "lastName" },
    };

    /// <summary>
    /// Whether this template asks for something this segment cannot give it,
    /// as a sentence, or null.
    /// </summary>
    /// <remarks>
    /// <see cref="TemplateRenderer"/> leaves an unfilled placeholder standing
    /// rather than emptying it, which is exactly right for the one message
    /// somebody is testing and exactly wrong for four hundred that cannot be
    /// recalled. This is the check that turns "an obvious mistake in a test"
    /// into "a refusal before the send".
    /// </remarks>
    private static string? Unfillable(EmailTemplate template, Segment segment)
    {
        var wanted = TemplateRenderer.PlaceholdersIn(template);
        var available = Fillable(segment);
        var missing = wanted.Where(p => !available.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();

        return missing.Count == 0
            ? null
            : string.Format(
                CultureInfo.InvariantCulture,
                "This template fills in {0}, which this segment does not carry. "
                + "Pick a different template or a different segment.",
                string.Join(", ", missing.Select(m => $"{{{{{m}}}}}")));
    }

    /// <summary>
    /// Whether some recipients would get a placeholder instead of a value, as
    /// a sentence, or null.
    /// </summary>
    /// <remarks>
    /// Different from <see cref="Unfillable"/>, and both are needed. That one
    /// asks whether the segment carries the field at all; this one asks
    /// whether every person in it actually has one — a row that was autosaved
    /// before somebody typed their name has an email and no first name, and a
    /// template greeting them would reach them as "Hi {{firstName}},".
    /// </remarks>
    private static string? Missing(
        EmailTemplate? template, IReadOnlyList<SegmentMember> members)
    {
        if (template is null || members.Count == 0)
        {
            return null;
        }

        var wanted = TemplateRenderer.PlaceholdersIn(template);
        var blank = 0;

        foreach (var member in members)
        {
            var values = Values(member);
            if (wanted.Any(p => !values.ContainsKey(p)))
            {
                blank++;
            }
        }

        return blank == 0
            ? null
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0:N0} of these recipients have nothing to fill one of the "
                + "template's blanks with, and would receive the blank itself. "
                + "Use a template that does not fill anything in, or a segment "
                + "where everybody has a name on file.",
                blank);
    }

    // -------------------------------------------------------------- shaping ---

    /// <summary>
    /// Said the same way at preview and at send.
    /// </summary>
    /// <remarks>
    /// The template a campaign was drafted against is data, and data can be
    /// edited or deleted between drafting and sending. Sending whatever now
    /// answers to that key is not what the approver looked at.
    /// </remarks>
    private const string MissingTemplate =
        "This campaign's template has changed or been removed. "
        + "Draft it again against the template you mean.";

    /// <summary>Said the same way wherever a segment is too big.</summary>
    private static readonly string TooMany = string.Format(
        CultureInfo.InvariantCulture,
        "That segment resolves to more than {0:N0} people, which is more than "
        + "this event has. Check the segment before sending it.",
        Segment.MaxRecipients);

    /// <summary>
    /// Reads the segment back off a stored campaign.
    /// </summary>
    /// <remarks>
    /// A failure here is our bug rather than the caller's: the document was
    /// written by <see cref="Segment.ToJson"/> from something that parsed. It
    /// answers 500 with a sentence rather than throwing, because the screen
    /// this reaches is one somebody is using to decide whether to mail four
    /// hundred people, and "that did not work" is a worse thing to show them
    /// than "this campaign cannot be read; make a new one".
    /// </remarks>
    private static bool TryStored(Campaign campaign, out Segment? segment, out IResult refusal)
    {
        segment = null;
        refusal = Results.Ok();

        if (campaign.Segment is { } stored)
        {
            try
            {
                using var document = JsonDocument.Parse(stored);
                if (Segment.TryParse(document.RootElement, out segment, out _))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                // Falls through to the refusal below.
            }
        }

        refusal = Results.Json(
            new
            {
                error = "This campaign's segment cannot be read, so it cannot be "
                        + "sent. Draft it again.",
            },
            statusCode: StatusCodes.Status500InternalServerError);

        return false;
    }

    /// <summary>The event a segment names, when it names one.</summary>
    /// <remarks>
    /// Only the status segment carries one directly. Reaching an event through
    /// a form id would mean this file querying <c>applications.forms</c>, which
    /// is another module's table; the column stays null rather than being
    /// filled in by a query that does not belong here.
    /// </remarks>
    private static Guid? EventOf(Segment segment) =>
        segment is Segment.InStatus status ? status.EventId : null;

    private static object Describe(Campaign campaign) => new
    {
        id = campaign.Id,
        name = campaign.Name,
        status = campaign.Status,
        templateKey = campaign.TemplateKey,
        templateKind = campaign.TemplateKind,
        eventId = campaign.EventId,

        // Handed back as JSON rather than as a string, so the console can put
        // it straight back into the form that made it.
        segment = Stored(campaign.Segment),
        recipientCount = campaign.RecipientCount,
        createdBy = campaign.CreatedBy,
        approvedBy = campaign.ApprovedBy,
        queuedAt = campaign.QueuedAt,
        completedAt = campaign.CompletedAt,
        createdAt = campaign.CreatedAt,
    };

    private static object Describe(CampaignProgress progress) => new
    {
        total = progress.Total,
        pending = progress.Pending,

        // Named for what it means rather than for a status. "Gone" is
        // everything that has left this system, whatever the provider then did
        // with it, and it is the number that decides whether cancelling is
        // still worth anything.
        gone = progress.Gone,
        byStatus = progress.ByStatus,
    };

    private static JsonElement? Stored(string? segment)
    {
        if (segment is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(segment);

            // Cloned, because the document is disposed on the way out of this
            // method and an un-cloned element is a window onto its buffer.
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
