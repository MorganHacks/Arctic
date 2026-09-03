using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Domain;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// The form builder's API.
/// </summary>
/// <remarks>
/// Two permissions, split the same way the people surface is.
/// <c>applications.view</c> reads: anybody who works the queue should be able
/// to see what was asked, because half of reading an answer is knowing the
/// question. <c>forms.manage</c> writes, and is a much smaller group — the
/// form is answered once by several hundred people and cannot be corrected
/// afterwards for the ones who already have.
/// <para>
/// Nothing here logs a label or an answer. Form ids, version numbers and
/// counts only, so a log that leaks says what was built and never what anybody
/// was asked to tell us.
/// </para>
/// </remarks>
public static class AdminFormEndpoints
{
    public static IEndpointRouteBuilder MapFormsAdmin(this IEndpointRouteBuilder app)
    {
        var forms = app.MapGroup("/admin/forms");

        forms.MapGet("", ListForms)
             .RequirePermission(Permission.ApplicationsView);
        forms.MapGet("/{id:guid}/draft", GetDraft)
             .RequirePermission(Permission.ApplicationsView);
        forms.MapGet("/{id:guid}/versions", GetHistory)
             .RequirePermission(Permission.ApplicationsView);

        forms.MapPost("", CreateForm)
             .RequirePermission(Permission.FormsManage);
        forms.MapPut("/{id:guid}/draft", SaveDraft)
             .RequirePermission(Permission.FormsManage);
        forms.MapPost("/{id:guid}/publish", Publish)
             .RequirePermission(Permission.FormsManage);

        // Who the form is for, which is not one of its questions. Behind
        // forms.manage with the rest of the writing, because narrowing an
        // audience closes a live form to people who were about to answer it.
        forms.MapPut("/{id:guid}/audience", SaveAudience)
             .RequirePermission(Permission.FormsManage);

        return app;
    }

    /// <summary>
    /// The bodies these endpoints take.
    /// </summary>
    /// <remarks>
    /// Nullable for the same reason PeopleEndpoints' are: minimal APIs bind
    /// the body before endpoint filters run, so a required body answers a
    /// request with none before the permission gate has looked at it. Optional
    /// here and checked in the handler means authorization answers first.
    /// </remarks>
    public sealed record CreateFormRequest(string? Name, string? Kind);

    public sealed record SaveDraftRequest(IReadOnlyList<FormField>? Fields);

    public sealed record AudienceRequest(
        bool? RequiresSignIn, IReadOnlyList<string>? EligibleStatuses);

    // ------------------------------------------------------------- reading ---

    /// <summary>
    /// The forms on an event, and the events there are to choose from.
    /// Requires <c>applications.view</c>.
    /// </summary>
    /// <remarks>
    /// The event list rides along rather than living behind its own endpoint,
    /// for the same reason the permission catalogue rides along with the
    /// teams: the console needs both to draw one screen, and a second round
    /// trip to fill in a dropdown is a waterfall for no benefit.
    /// <para>
    /// With no event named it answers for the most recent one, which is the
    /// one being run. Refusing instead would mean the console could not link
    /// to its own forms screen without already knowing an id.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ListForms(
        Guid? eventId,
        IFormStore forms,
        IEventStore events,
        CancellationToken ct)
    {
        var all = await events.ListAsync(ct);
        if (all.Count == 0)
        {
            return Results.Ok(new { events = all, chosen = (object?)null, forms = Array.Empty<object>() });
        }

        var chosen = all.FirstOrDefault(e => e.Id == eventId) ?? all[0];
        var listed = await forms.ForEventAsync(chosen.Id, ct);

        // One extra query per form, and deliberately so. A form's live version
        // is what the list is for — "is this the one on the flyer" is the
        // question somebody opens this screen to answer — and an event has a
        // handful of forms, not a page of them. The moment that stops being
        // true this wants a join in the store rather than a loop here.
        var rows = new List<object>(listed.Count);
        foreach (var form in listed)
        {
            var published = await forms.PublishedAsync(form.Id, ct);
            rows.Add(new
            {
                id = form.Id,
                code = form.Code,
                name = form.Name,
                kind = form.Kind,
                closesAt = form.ClosesAt,
                requiresSignIn = form.RequiresSignIn,
                eligibleStatuses = form.EligibleStatuses,
                published = published is not null,
                publishedVersion = published?.Version,
                questions = published is null ? (int?)null : Questions(published.Fields),
            });
        }

        return Results.Ok(new
        {
            events = all.Select(Describe),
            chosen = Describe(chosen),
            forms = rows,
        });
    }

    /// <summary>
    /// The draft being edited, plus everything the builder needs to draw
    /// itself. Requires <c>applications.view</c>.
    /// </summary>
    /// <remarks>
    /// Asking for the draft creates one if none exists, seeded from whatever
    /// is published or from MLH's questions. That happens on a GET, which is
    /// not something to do lightly — but the alternative is a builder that
    /// shows nothing until somebody presses a button whose only honest label
    /// would be "start editing", and the whole screen is that button.
    /// <para>
    /// <c>locked</c> ships as its own list rather than being inferred from the
    /// flag on each field. The flag says what this draft happens to record;
    /// the list says what the server will enforce on save, and those are the
    /// same thing only when nothing has gone wrong.
    /// </para>
    /// </remarks>
    private static async Task<IResult> GetDraft(
        Guid id, HttpContext http, IFormStore forms, CancellationToken ct)
    {
        var form = await Find(forms, id, ct);
        if (form is null)
        {
            return Results.NotFound(new { error = "No such form." });
        }

        // The person is recorded as the draft's author only when they are the
        // one who caused it to exist. Reading somebody else's draft does not
        // make it theirs, and DraftAsync only uses this on creation.
        var draft = await forms.DraftAsync(id, http.PersonId(), ct);
        var published = await forms.PublishedAsync(id, ct);

        return Results.Ok(new
        {
            form = Describe(form),
            draft = new
            {
                id = draft.Id,
                version = draft.Version,
                fields = draft.Fields,
            },
            published = published is null ? null : new
            {
                version = published.Version,
                publishedAt = published.PublishedAt,
            },
            locked = LockedFields.Keys.OrderBy(k => k),

            // The statuses an audience can be built from, so the builder
            // offers the real set rather than asking somebody to type one.
            // Ships with the draft for the same reason the event list ships
            // with the forms list: it is needed to draw one screen, and a
            // second round trip to fill a checkbox group is a waterfall for no
            // benefit.
            statuses = EligibleStatuses.All,
        });
    }

    /// <summary>Every version, newest first. Requires <c>applications.view</c>.</summary>
    private static async Task<IResult> GetHistory(
        Guid id, IFormStore forms, CancellationToken ct)
    {
        var form = await Find(forms, id, ct);
        if (form is null)
        {
            return Results.NotFound(new { error = "No such form." });
        }

        var history = await forms.HistoryAsync(id, ct);

        // Counts rather than the questions themselves. This feeds a sidebar
        // that answers "when did this change and by how much"; shipping every
        // field of every version to draw it would be several hundred
        // kilobytes to render one line each.
        return Results.Ok(new
        {
            versions = history.Select(v => new
            {
                version = v.Version,
                status = v.Status,
                questions = Questions(v.Fields),
                createdAt = v.CreatedAt,
                publishedAt = v.PublishedAt,
            }),
        });
    }

    // ------------------------------------------------------------- writing ---

    /// <summary>Requires <c>forms.manage</c>.</summary>
    private static async Task<IResult> CreateForm(
        CreateFormRequest? request,
        Guid? eventId,
        HttpContext http,
        IFormStore forms,
        IEventStore events,
        ILogger<CreateFormRequest> log,
        CancellationToken ct)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Results.BadRequest(new { error = "A form needs a name." });
        }

        // Only the two the schema's check constraint allows. Letting anything
        // else through would fail in the database as a 500, which tells the
        // person filling in the form nothing they can act on.
        var kind = request?.Kind?.Trim() ?? "survey";
        if (kind is not ("application" or "survey"))
        {
            return Results.BadRequest(new { error = "A form is an application or a survey." });
        }

        var all = await events.ListAsync(ct);
        var chosen = all.FirstOrDefault(e => e.Id == eventId) ?? all.FirstOrDefault();
        if (chosen is null)
        {
            return Results.BadRequest(new { error = "There is no event to put a form on." });
        }

        Form form;
        try
        {
            form = await forms.CreateAsync(chosen.Id, name, kind, http.PersonId(), ct);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505")
        {
            // The only unique index a well-formed request can hit: one
            // application form per event. Codes are retried inside the store.
            return Results.Conflict(new
            {
                error = "This event already has an application form. "
                        + "Edit that one, or add a survey instead.",
            });
        }

        log.LogInformation(
            "Form created. {actor} {form} {kind} {event}",
            http.PersonId(), form.Id, kind, Events.FormCreated);

        return Results.Created($"/admin/forms/{form.Id}", Describe(form));
    }

    /// <summary>
    /// Saves the draft's questions. Requires <c>forms.manage</c>.
    /// </summary>
    /// <remarks>
    /// The whole array every time, because that is what the builder holds and
    /// a per-question patch protocol would need an ordering and a conflict
    /// story neither this screen nor its one editor at a time has.
    /// <para>
    /// Every save goes through <see cref="LockedFields.Reconcile"/> first. The
    /// builder disables the controls on MLH's questions, but a disabled input
    /// is a suggestion — this is the part that holds.
    /// </para>
    /// </remarks>
    private static async Task<IResult> SaveDraft(
        Guid id,
        SaveDraftRequest? request,
        HttpContext http,
        IFormStore forms,
        ILogger<SaveDraftRequest> log,
        CancellationToken ct)
    {
        if (request?.Fields is null)
        {
            return Results.BadRequest(new { error = "The draft's questions are required." });
        }

        var form = await Find(forms, id, ct);
        if (form is null)
        {
            return Results.NotFound(new { error = "No such form." });
        }

        var reconciled = LockedFields.Reconcile(request.Fields, form.IsApplication);
        if (reconciled.Fields is null)
        {
            return Problems(
                "This draft could not be saved.", reconciled.Problems);
        }

        // Makes sure there is a draft row to write into. Without it a save
        // against a form nobody has opened updates nothing and answers 204,
        // which looks exactly like success.
        await forms.DraftAsync(id, http.PersonId(), ct);
        await forms.SaveDraftAsync(id, reconciled.Fields, ct);

        log.LogInformation(
            "Draft saved. {actor} {form} {questions} {event}",
            http.PersonId(), id, reconciled.Fields.Count, Events.FormDraftSaved);

        // The problems the draft still has, so the builder can show them as it
        // goes without waiting for a refused publish. Advisory: publishing
        // checks again, in the store, inside the same transaction that writes.
        return Results.Ok(new
        {
            saved = reconciled.Fields.Count,
            problems = FormValidation.Check(reconciled.Fields, form.IsApplication).Select(Describe),
        });
    }

    /// <summary>
    /// Makes the draft the live form. Requires <c>forms.manage</c>.
    /// </summary>
    /// <remarks>
    /// 400 with every problem at once when it is refused, each carrying the
    /// key of the question it belongs to. One at a time turns fixing a form
    /// into a guessing game where each fix reveals the next complaint, and a
    /// problem with no key attached is one the author has to go hunting for.
    /// </remarks>
    private static async Task<IResult> Publish(
        Guid id,
        HttpContext http,
        IFormStore forms,
        ILogger<Form> log,
        CancellationToken ct)
    {
        var form = await Find(forms, id, ct);
        if (form is null)
        {
            return Results.NotFound(new { error = "No such form." });
        }

        FormVersion published;
        try
        {
            published = await forms.PublishAsync(id, http.PersonId(), ct);
        }
        catch (FormNotPublishableException refused)
        {
            return Problems(
                "This form is not ready to go in front of applicants.", refused.Problems);
        }

        log.LogInformation(
            "Form published. {actor} {form} {version} {questions} {event}",
            http.PersonId(), id, published.Version, Questions(published.Fields),
            Events.FormPublished);

        return Results.Ok(new
        {
            version = published.Version,
            publishedAt = published.PublishedAt,
            questions = Questions(published.Fields),
        });
    }

    /// <summary>
    /// Sets who a form is for. Requires <c>forms.manage</c>.
    /// </summary>
    /// <remarks>
    /// Both halves in one request, because they are one decision: a gate with
    /// no audience is a form nobody can open, and the schema refuses to store
    /// that combination rather than leaving it to be discovered by the people
    /// it locks out.
    /// <para>
    /// The application form is refused outright and the sentence says why.
    /// Gating it makes applying impossible — the account it would demand is
    /// created by applying — and a check constraint refuses the same thing at
    /// the database, so this is the half that gives an author a reason rather
    /// than a 500.
    /// </para>
    /// </remarks>
    private static async Task<IResult> SaveAudience(
        Guid id,
        AudienceRequest? request,
        HttpContext http,
        IFormStore forms,
        ILogger<AudienceRequest> log,
        CancellationToken ct)
    {
        if (request?.RequiresSignIn is not { } requiresSignIn)
        {
            return Results.BadRequest(new { error = "Say whether this form requires sign-in." });
        }

        var form = await Find(forms, id, ct);
        if (form is null)
        {
            return Results.NotFound(new { error = "No such form." });
        }

        if (form.IsApplication && requiresSignIn)
        {
            return Results.BadRequest(new
            {
                error = "The application form cannot require sign-in. "
                        + "Applying is how somebody gets an account.",
            });
        }

        var statuses = request.EligibleStatuses ?? [];

        if (!EligibleStatuses.AllKnown(statuses))
        {
            return Results.BadRequest(new { error = "That is not an application status." });
        }

        if (requiresSignIn && statuses.Count == 0)
        {
            // Refused rather than read as "everybody" or as "nobody". An empty
            // list has both readings and the wrong one is chosen silently, on
            // the form that decides who gets fed.
            return Results.BadRequest(new
            {
                error = "Choose which applicants can open this form.",
            });
        }

        var saved = await forms.SaveAudienceAsync(id, requiresSignIn, statuses, ct);
        if (saved is null)
        {
            return Results.NotFound(new { error = "No such form." });
        }

        log.LogInformation(
            "Form audience saved. {actor} {form} {gated} {statuses}",
            http.PersonId(), id, requiresSignIn, saved.EligibleStatuses.Count);

        return Results.Ok(Describe(saved));
    }

    // ------------------------------------------------------------- shaping ---

    /// <summary>
    /// A refusal the builder can put against the right question.
    /// </summary>
    /// <remarks>
    /// <c>error</c> as well as <c>problems</c> so that <c>apiWrite</c> in the
    /// console — which reads <c>error</c> and knows nothing about this shape —
    /// still has a sentence to show. A caller that understands the list gets
    /// the list; one that does not is not left with "That did not work."
    /// </remarks>
    private static IResult Problems(string summary, IReadOnlyList<FormProblem> problems) =>
        Results.BadRequest(new
        {
            error = summary,
            problems = problems.Select(Describe),
        });

    /// <summary>
    /// How many questions a version actually asks.
    /// </summary>
    /// <remarks>
    /// Not the length of the array. A page break sits in the same list and is
    /// not something anybody answers, so counting one would make "12 questions"
    /// on the forms list a number that does not match what an applicant is
    /// asked — and this number is read precisely to check that it does.
    /// </remarks>
    private static int Questions(IReadOnlyList<FormField> fields) =>
        fields.Count(f => f.Type != FieldType.Section);

    private static object Describe(FormProblem problem) => new
    {
        message = problem.Message,
        fieldKey = problem.FieldKey,
    };

    private static object Describe(Form form) => new
    {
        id = form.Id,
        eventId = form.EventId,
        code = form.Code,
        name = form.Name,
        kind = form.Kind,
        closesAt = form.ClosesAt,
        requiresSignIn = form.RequiresSignIn,
        eligibleStatuses = form.EligibleStatuses,
    };

    private static object Describe(EventSummary summary) => new
    {
        id = summary.Id,
        slug = summary.Slug,
        name = summary.Name,
        startsAt = summary.StartsAt,
    };

    /// <summary>
    /// The form behind an id, or null.
    /// </summary>
    /// <remarks>
    /// Every route looks this up before doing anything, which costs a query
    /// and buys the difference between "no such form" and a draft quietly
    /// created against an id somebody mistyped. <see cref="IFormStore.DraftAsync"/>
    /// does not check, because it has no reason to — it is called on ids that
    /// came from the database. These ids come from a URL bar.
    /// </remarks>
    private static Task<Form?> Find(IFormStore forms, Guid id, CancellationToken ct) =>
        forms.ByIdAsync(id, ct);
}
