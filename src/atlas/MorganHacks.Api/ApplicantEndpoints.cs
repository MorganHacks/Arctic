using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// The screen registration lives in: find an applicant, read one, decide one.
/// </summary>
/// <remarks>
/// Deliberately overlapping with <see cref="FormResponseEndpoints"/> and
/// deliberately not the same surface. That one is arranged by form — a table
/// of submissions with a column per question, which is the shape you want when
/// the question is "what did people answer". This one is arranged by person,
/// because the question here is "who is this and what happens to them next",
/// and the answers are one panel of that rather than the whole screen.
/// <para>
/// Four permissions, and the split is the schema's rather than this file's.
/// <c>applications.view</c> gates the list and the header — comms holds it to
/// build segments and logistics for headcount, and a name, a school and a
/// status is what those need. <c>applications.view_responses</c> gates the
/// answers, checked inside the handler so this cannot become a second way to
/// read several hundred essays without the permission that exists to guard
/// them. <c>applications.view_resume</c> gates the signed link, for the same
/// reason and in the same place. <c>applications.decide</c> gates the status
/// change, which is the permission <c>decided_by</c> on the table has always
/// implied.
/// </para>
/// <para>
/// <c>applications.bulk_decide</c> appears nowhere here. There is no bulk
/// action on this screen, and a route that quietly accepted a list of ids
/// under the single-decision permission would be exactly the escalation that
/// permission was split out to prevent.
/// </para>
/// <para>
/// Notes are behind <c>applications.note</c> on both the read and the write.
/// The schema calls them internal and never shown to the applicant, which is
/// only true if reading them is as narrow as writing them —
/// <c>applications.view</c> is a much larger group than the words suggest, and
/// one reviewer's private opinion of somebody is not headcount data.
/// </para>
/// <para>
/// Nothing here logs a name, an address, an answer or a note. Person ids,
/// application ids and statuses, which is enough to find a row and tells a log
/// reader nothing about who anybody is.
/// </para>
/// </remarks>
public static class ApplicantEndpoints
{
    /// <summary>How many applicants a page holds when the caller says nothing.</summary>
    /// <remarks>
    /// About two screens of rows. Small enough that the first page is up
    /// quickly on the morning registration closes, large enough that reading
    /// several hundred is a handful of clicks.
    /// </remarks>
    private const int DefaultLimit = 50;

    /// <summary>And the most it will hold however loudly they ask.</summary>
    private const int MaxLimit = 200;

    /// <summary>
    /// As long as a decision reason may be.
    /// </summary>
    /// <remarks>
    /// A sentence, not an essay. This lands in an append-only history row that
    /// can never be edited, and something long enough to need editing belongs
    /// in a note, which can be added to.
    /// </remarks>
    private const int MaxReason = 500;

    /// <summary>And a note.</summary>
    private const int MaxNote = 4000;

    public static IEndpointRouteBuilder MapApplicants(this IEndpointRouteBuilder app)
    {
        var applicants = app.MapGroup("/admin/applicants");

        applicants.MapGet("", List)
                  .RequirePermission(Permission.ApplicationsView);
        applicants.MapGet("/{id:guid}", One)
                  .RequirePermission(Permission.ApplicationsView);

        applicants.MapPost("/{id:guid}/status", ChangeStatus)
                  .RequirePermission(Permission.ApplicationsDecide);
        applicants.MapPost("/{id:guid}/notes", AddNote)
                  .RequirePermission(Permission.ApplicationsNote);

        return app;
    }

    /// <summary>
    /// The bodies these endpoints take.
    /// </summary>
    /// <remarks>
    /// Nullable, and checked in the handler rather than required on the
    /// parameter. Minimal APIs bind the body before endpoint filters run, so a
    /// required body turns a request with none into a 400 decided before the
    /// permission gate has looked at it — an unauthenticated caller learning
    /// "that route exists and wants JSON" rather than "sign in".
    /// </remarks>
    public sealed record StatusRequest(string? Status, string? Reason);

    public sealed record NoteRequest(string? Body);

    // ------------------------------------------------------------- reading ---

    /// <summary>
    /// A page of applicants, newest first. Requires <c>applications.view</c>.
    /// </summary>
    /// <remarks>
    /// The events ride along with the page, and the counts by status ride along
    /// with both. Same reasoning as the forms list next door: the console needs
    /// all three to draw one screen, and three round trips to fill in a picker,
    /// a set of filter counts and a table is a waterfall for no benefit.
    /// <para>
    /// With no event named it answers for the most recent one, which is the one
    /// being run. Refusing instead would mean the console could not link to its
    /// own applicants screen without already knowing an id.
    /// </para>
    /// <para>
    /// The counts are of the whole event and not of the filtered set, on
    /// purpose. They are what the filters are chosen from — "how many are still
    /// undecided" is the question that decides which filter to press — and
    /// counts that moved with the filter would only ever confirm what the
    /// filter already said.
    /// </para>
    /// </remarks>
    private static async Task<IResult> List(
        HttpContext http,
        IApplicantStore applicants,
        IEventStore events,
        CancellationToken ct,
        Guid? eventId = null,
        string? q = null,
        string? cursor = null,
        int limit = DefaultLimit)
    {
        var all = await events.ListAsync(ct);
        if (all.Count == 0)
        {
            return Results.Ok(new
            {
                events = all,
                chosen = (object?)null,
                counts = new Dictionary<string, int>(),
                items = Array.Empty<object>(),
                nextCursor = (string?)null,
            });
        }

        var chosen = all.FirstOrDefault(e => e.Id == eventId) ?? all[0];

        if (!TryCursor(cursor, out var after))
        {
            // Refused rather than ignored. A cursor we cannot read means the
            // caller is somewhere in the middle of a list, and starting them
            // silently at the top would read as the newest page arriving
            // twice.
            return Results.BadRequest(new { error = "That page marker is not one of ours." });
        }

        // Read off the query directly rather than bound to a parameter,
        // because this one repeats: ?status=accepted&status=waitlisted is one
        // filter with two values, and that is the shape every useful filter on
        // this screen has.
        if (!TryStatuses(http.Request.Query["status"], out var statuses))
        {
            // A status this codebase cannot name is not a filter that matches
            // nothing — it is a caller asking for something we have
            // misunderstood, and answering with an empty list would read as
            // "there are none of those".
            return Results.BadRequest(new { error = "No such status." });
        }

        var page = await applicants.PageAsync(
            new ApplicantSearch(chosen.Id, q, statuses), after, Clamp(limit), ct);

        var counts = await applicants.CountsAsync(chosen.Id, ct);

        return Results.Ok(new
        {
            events = all,
            chosen,
            counts = counts.ToDictionary(c => c.Key.ToWire(), c => c.Value),
            items = page.Items.Select(Describe),
            nextCursor = Encode(page.Next),
        });
    }

    /// <summary>
    /// One applicant in full. Requires <c>applications.view</c>, plus
    /// <c>applications.view_responses</c> for the answers,
    /// <c>applications.view_resume</c> for the resume link and
    /// <c>applications.note</c> for the notes.
    /// </summary>
    /// <remarks>
    /// Three of the four permissions are checked here rather than on the route
    /// because the rest of the record is readable without them. Somebody
    /// holding view and nothing else sees who this is, where they have got to
    /// and how they got there, with <c>answers</c>, <c>resume</c> and
    /// <c>notes</c> null — null rather than empty, so the screen can say "you
    /// cannot see this" instead of "there is nothing here", which are
    /// different sentences and only one of them is true.
    /// <para>
    /// Reading a resume leaves the same mark it leaves through
    /// <see cref="ResumeEndpoints"/>. The permission model calls a resume more
    /// sensitive than the rest of a record, which is only true if every path to
    /// one is recorded, and a third path that logged nothing would quietly undo
    /// that.
    /// </para>
    /// <para>
    /// <c>allowedNext</c> comes from <see cref="StatusTransition"/> rather than
    /// from a list written again in TypeScript. The lifecycle already exists
    /// and is already the specification; a console that offered a move the API
    /// refuses would be a button whose only outcome is an error message.
    /// </para>
    /// </remarks>
    private static async Task<IResult> One(
        Guid id,
        HttpContext http,
        IApplicantStore applicants,
        IApplicationStore applications,
        IFormStore forms,
        IResponseStore responses,
        IResumeStore resumes,
        PermissionService permissions,
        ILogger<Applicant> log,
        CancellationToken ct)
    {
        var applicant = await applicants.ByIdAsync(id, ct);
        if (applicant is null)
        {
            return Results.NotFound(new { error = "No such applicant." });
        }

        // A second permission lookup, after the gate on the route already did
        // one. Worth a query: the alternative is the filter stashing the whole
        // permission set on the request for handlers to rummage through, and a
        // handler that can read any permission is one that will eventually
        // check the wrong one.
        var effective = await permissions.ForAsync(http.PersonId(), ct);

        var history = await applications.HistoryOfAsync(id, ct);

        var answers = effective.Can(Permission.ApplicationsViewResponses)
            ? await AnswersOf(applicant, forms, responses, ct)
            : null;

        var notes = effective.Can(Permission.ApplicationsNote)
            ? (await applicants.NotesOfAsync(id, ct)).Select(Describe).ToList()
            : null;

        object? resume = null;
        if (applicant.HasResume && effective.Can(Permission.ApplicationsViewResume))
        {
            resume = await ResumeOf(id, applications, resumes, http.PersonId(), log, ct);
        }

        return Results.Ok(new
        {
            id = applicant.Id,
            eventId = applicant.EventId,
            email = applicant.Email,
            firstName = applicant.FirstName,
            lastName = applicant.LastName,
            school = applicant.School,

            // The stored spelling, not the enum's name. The API serialises
            // enums as camel-cased member names, which would put "underReview"
            // on the wire against an "under_review" in the column — two
            // spellings of one status is one of them being wrong somewhere.
            status = applicant.Status.ToWire(),
            allowedNext = StatusTransition.From(applicant.Status).Select(s => s.ToWire()),

            formVersion = applicant.FormVersion,
            createdAt = applicant.CreatedAt,
            submittedAt = applicant.SubmittedAt,
            decidedAt = applicant.DecidedAt,
            rsvpDeadline = applicant.RsvpDeadline,
            confirmedAt = applicant.ConfirmedAt,
            declinedAt = applicant.DeclinedAt,
            checkedInAt = applicant.CheckedInAt,

            hasResume = applicant.HasResume,
            resume,

            history = history.Select(Describe),

            // Null where this person may not read them, empty where there are
            // none. The screen says something different for each.
            answers,
            notes,
        });
    }

    // ------------------------------------------------------------- writing ---

    /// <summary>
    /// Moves an applicant to a new status. Requires
    /// <c>applications.decide</c>.
    /// </summary>
    /// <remarks>
    /// Everything that makes this correct already exists and this handler's
    /// job is to not go around any of it.
    /// <list type="bullet">
    /// <item><see cref="StatusTransition"/> decides whether the move is legal,
    /// and it is the only table of that in the system.</item>
    /// <item><see cref="IApplicationStore.TransitionAsync"/> is the only way to
    /// write a status. It takes the row lock, sets <c>app.actor_id</c> as a
    /// transaction-local setting, and lets the trigger write the history row —
    /// so the actor on the trail is this person rather than a null, and the
    /// decision and its record commit together or neither does.</item>
    /// <item>The lifecycle timestamps are the trigger's too. Nothing here
    /// stamps <c>decided_at</c>, because a handler that did would be a second
    /// opinion about when a decision happened.</item>
    /// </list>
    /// <para>
    /// No batch id. That column is for a bulk action, this is not one, and
    /// filling it in would make a single decision indistinguishable from one
    /// of four hundred when somebody comes to undo the four hundred.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ChangeStatus(
        Guid id,
        StatusRequest? request,
        HttpContext http,
        IApplicationStore applications,
        ILogger<Applicant> log,
        CancellationToken ct)
    {
        if (!TryStatus(request?.Status, out var next))
        {
            return Results.BadRequest(new { error = "No such status." });
        }

        var reason = Trimmed(request!.Reason);
        if (reason is { Length: > MaxReason })
        {
            return Results.BadRequest(new
            {
                error = $"A reason has to be {MaxReason} characters or fewer. "
                        + "Anything longer belongs in a note.",
            });
        }

        StatusChange change;
        try
        {
            change = await applications.TransitionAsync(
                id, next, actorId: http.PersonId(), reason: reason, ct: ct);
        }
        catch (InvalidTransitionException e)
        {
            // 409 rather than 400: the request was well formed and the
            // application is simply not where the caller thought it was —
            // which on a shared queue usually means somebody else moved it
            // first. The current status and what it can still do go back so
            // the screen can correct itself rather than just apologise.
            return Results.Conflict(new
            {
                error = $"An application cannot go from {e.From.ToWire()} to {e.To.ToWire()}.",
                status = e.From.ToWire(),
                allowedNext = StatusTransition.From(e.From).Select(s => s.ToWire()),
            });
        }
        catch (InvalidOperationException)
        {
            // The store's answer for an id that names no application. Caught
            // after the transition case above, which derives from this one.
            return Results.NotFound(new { error = "No such applicant." });
        }

        // Who moved whom, and between which two statuses. Not the reason,
        // which is a sentence somebody wrote about a person — the history row
        // holds that, and it is behind a permission where a log line is not.
        log.LogInformation(
            "An application changed status. {actor} {applicationId} {from} {to} {event}",
            http.PersonId(), id, change.From?.ToWire(), change.To.ToWire(),
            Events.ApplicationStatusChanged);

        return Results.Ok(new
        {
            status = change.To.ToWire(),
            allowedNext = StatusTransition.From(change.To).Select(s => s.ToWire()),
            change = Describe(change),
        });
    }

    /// <summary>
    /// Adds an internal note. Requires <c>applications.note</c>.
    /// </summary>
    /// <remarks>
    /// Notes are a concept the schema already has, with a table, an author and
    /// a permission of its own. Nothing is being invented here — this is the
    /// only way to write a row into it.
    /// <para>
    /// There is no edit and no delete, and that is not an omission to fill in
    /// later. A note is one reviewer's contemporaneous opinion of an applicant;
    /// a version of it that can be rewritten after the decision it justified is
    /// worth less than no note at all.
    /// </para>
    /// </remarks>
    private static async Task<IResult> AddNote(
        Guid id,
        NoteRequest? request,
        HttpContext http,
        IApplicantStore applicants,
        CancellationToken ct)
    {
        var body = Trimmed(request?.Body);
        if (body is null)
        {
            return Results.BadRequest(new { error = "A note cannot be empty." });
        }

        if (body.Length > MaxNote)
        {
            return Results.BadRequest(new
            {
                error = $"A note has to be {MaxNote} characters or fewer.",
            });
        }

        var note = await applicants.AddNoteAsync(id, http.PersonId(), body, ct);
        if (note is null)
        {
            return Results.NotFound(new { error = "No such applicant." });
        }

        return Results.Created($"/admin/applicants/{id}", Describe(note));
    }

    // -------------------------------------------------------------- reading ---

    /// <summary>
    /// What this applicant answered, joined back to the questions.
    /// </summary>
    /// <remarks>
    /// Joined here rather than on the screen, unlike the responses table next
    /// door. That one draws a column per question and needs the whole form
    /// definition anyway; this one shows one person, and shipping every
    /// version of a twenty-question form to label twenty answers is a lot of
    /// payload to make the browser do a join the server already can.
    /// <para>
    /// Published order, and every published question whether or not it was
    /// answered — an absent answer is a fact about this applicant, and a list
    /// that skipped it would read as a question nobody was asked. Answers
    /// under keys the form no longer publishes come last with no label, because
    /// the wording was deleted with the question and the key is all that is
    /// left of it. Those are still somebody's words.
    /// </para>
    /// <para>
    /// Empty for an application nobody submitted. The row exists from the
    /// moment somebody starts the form, so a half-filled draft is an ordinary
    /// thing to open, and it has no answers to show rather than being an error.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<object>> AnswersOf(
        Applicant applicant,
        IFormStore forms,
        IResponseStore responses,
        CancellationToken ct)
    {
        // The application form on this event, of which the schema allows
        // exactly one. Surveys on the same event answer nothing about an
        // applicant and are stored somewhere else entirely.
        var form = (await forms.ForEventAsync(applicant.EventId, ct))
            .FirstOrDefault(f => f.IsApplication);

        if (form is null)
        {
            return [];
        }

        // Every version, not the published one. A question moved between
        // columns, or rebuilt under a new key, would otherwise file this
        // person's school under a heading they never answered.
        var questions = FormQuestions.From(await forms.HistoryAsync(form.Id, ct));

        var response = await responses.ByIdAsync(
            applicant.EventId, applicant.Id, questions, ct);

        if (response is null)
        {
            return [];
        }

        var shown = new List<object>();
        var published = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in questions.Published)
        {
            // The file question never has an answer — its answer was an upload
            // ticket spent at submit, and what it became is the resume beside
            // this list. A page break was never a question at all.
            if (field.Type is FieldType.File or FieldType.Section)
            {
                continue;
            }

            published.Add(field.Key);

            shown.Add(new
            {
                key = field.Key,
                label = field.Label,
                value = response.Answers.TryGetValue(field.Key, out var answer)
                    ? answer
                    : (JsonElement?)null,
            });
        }

        foreach (var leftover in response.Answers
                     .Where(a => !published.Contains(a.Key))
                     .OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            shown.Add(new
            {
                key = leftover.Key,
                label = (string?)null,
                value = (JsonElement?)leftover.Value,
            });
        }

        return shown;
    }

    /// <summary>
    /// A signed link to this applicant's resume, or null.
    /// </summary>
    /// <remarks>
    /// Null covers the object store being unconfigured and the object having
    /// gone missing. Neither is a reason to refuse somebody the rest of the
    /// record they came to read, and both are already loud in the log — the
    /// second one especially, because it means bytes we told an applicant we
    /// had are not there.
    /// </remarks>
    private static async Task<object?> ResumeOf(
        Guid id,
        IApplicationStore applications,
        IResumeStore resumes,
        Guid actor,
        ILogger log,
        CancellationToken ct)
    {
        var stored = await applications.ResumeOfAsync(id, ct);
        if (stored is null || !resumes.Available)
        {
            if (stored is not null)
            {
                log.LogError(
                    "A resume was asked for and there is no object store configured. "
                    + "{applicationId}",
                    id);
            }

            return null;
        }

        try
        {
            var link = await resumes.LinkToAsync(
                stored.StorageKey, ResumeFile.DownloadName(id), ct);

            // Who read whose, and when. The permission model calls a resume
            // more sensitive than the rest of an application, which is only
            // true if reading one leaves a record. The filename is not in it.
            log.LogInformation(
                "A resume was read. {actor} {applicationId} {event}",
                actor, id, Events.ResumeRead);

            return new
            {
                // The storage key is not here, on any path. What goes out is a
                // name to show, a size to show beside it, and a URL that is
                // dead in five minutes.
                filename = stored.Filename,
                sizeBytes = stored.Size,
                url = link.Url,

                // Handed over so the screen embedding the file knows when to
                // ask for a fresh one, rather than discovering the problem as
                // a broken frame.
                expiresAt = link.ExpiresAt,
            };
        }
        catch (ResumeMissingException)
        {
            // The row says there are bytes and the store disagrees. Loud,
            // because it means an object went missing rather than that a
            // reviewer asked for something reasonable and got no.
            log.LogError(
                "An application points at a resume the store does not have. {applicationId}",
                id);

            return null;
        }
    }

    // -------------------------------------------------------------- shaping ---

    /// <summary>
    /// One applicant as a row on the list.
    /// </summary>
    /// <remarks>
    /// Less than the detail carries, and the difference is the point: a list
    /// that ships everything is a list that reads several hundred people's full
    /// records to draw a table nobody is looking at most of. What is here is
    /// what somebody scans for — who, where from, where they have got to, and
    /// whether there is a file worth opening.
    /// </remarks>
    private static object Describe(Applicant applicant) => new
    {
        id = applicant.Id,
        email = applicant.Email,
        firstName = applicant.FirstName,
        lastName = applicant.LastName,
        school = applicant.School,
        status = applicant.Status.ToWire(),
        createdAt = applicant.CreatedAt,
        submittedAt = applicant.SubmittedAt,
        decidedAt = applicant.DecidedAt,
        hasResume = applicant.HasResume,
    };

    /// <summary>
    /// One step in an application's life.
    /// </summary>
    /// <remarks>
    /// The actor is an id and may be null, which is the honest record rather
    /// than a gap: the applicant did it themselves, the expiry job did it, or
    /// somebody fixed a row by hand — and putting a name against a decision
    /// nobody made is worse than admitting there is not one. Resolving the ids
    /// that are there to people is the console's job, through an endpoint gated
    /// on <c>people.view</c>.
    /// </remarks>
    private static object Describe(StatusChange change) => new
    {
        from = change.From?.ToWire(),
        to = change.To.ToWire(),
        actorId = change.ActorId,
        reason = change.Reason,
        batchId = change.BatchId,
        at = change.At,
    };

    private static object Describe(ApplicantNote note) => new
    {
        id = note.Id,
        authorId = note.AuthorId,
        body = note.Body,
        createdAt = note.CreatedAt,
    };

    private static int Clamp(int limit) =>
        limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // -------------------------------------------------------------- statuses ---

    /// <summary>
    /// Reads a status off the wire, or refuses.
    /// </summary>
    /// <remarks>
    /// Against <see cref="ApplicationStatuses.ToWire"/> rather than against the
    /// enum's member names, because the stored spelling is the contract and the
    /// member name is an implementation detail that renaming would change.
    /// <para>
    /// A TryParse rather than <see cref="ApplicationStatuses.Parse"/>, which
    /// throws. A status arriving from a caller is untrusted input and deserves
    /// a 400; that one is for reading a column we wrote ourselves, where an
    /// unrecognised value really is something to stop on.
    /// </para>
    /// </remarks>
    private static bool TryStatus(string? value, out ApplicationStatus status)
    {
        foreach (var candidate in Enum.GetValues<ApplicationStatus>())
        {
            if (candidate.ToWire() == value)
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }

    /// <summary>Reads a repeated <c>?status=</c> filter, or refuses.</summary>
    private static bool TryStatuses(
        IEnumerable<string?> values, out IReadOnlyList<ApplicationStatus> statuses)
    {
        var parsed = new List<ApplicationStatus>();

        foreach (var value in values)
        {
            if (!TryStatus(value, out var status))
            {
                statuses = [];
                return false;
            }

            // The same status twice is a caller repeating themselves, not an
            // error. ANY() over a list with duplicates in it means the same
            // thing, but sending them is untidy in a log.
            if (!parsed.Contains(status))
            {
                parsed.Add(status);
            }
        }

        statuses = parsed;
        return true;
    }

    // ---------------------------------------------------------------- cursor ---

    /// <summary>
    /// Where the next page starts, as one opaque string.
    /// </summary>
    /// <remarks>
    /// Opaque so that the ordering stays ours. A caller that can read
    /// "timestamp, id" out of a cursor is a caller who will eventually
    /// construct one, and then the ordering columns are a public API that
    /// cannot be changed without breaking whoever did. It is not a secret —
    /// everything it names is already in the page it came with — so it is
    /// encoded rather than signed.
    /// </remarks>
    private static string? Encode(ApplicantCursor? cursor) => cursor is not { } at
        ? null
        : Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            $"{at.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{at.Id:N}"));

    /// <summary>
    /// Reads one back, or refuses.
    /// </summary>
    /// <remarks>
    /// True with a null cursor means "start at the top", which is what no
    /// cursor at all means. False means one arrived and was not ours, and the
    /// caller is told so rather than quietly restarted.
    /// </remarks>
    private static bool TryCursor(string? value, out ApplicantCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value)).Split(':');

            if (parts.Length != 2
                || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var ticks)
                || ticks < 0 || ticks > DateTimeOffset.MaxValue.UtcTicks
                || !Guid.TryParseExact(parts[1], "N", out var id))
            {
                return false;
            }

            cursor = new ApplicantCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
