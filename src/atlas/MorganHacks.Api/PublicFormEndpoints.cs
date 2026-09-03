using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Services;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// The public face of a form: <c>forms.morganhacks.com/&lt;code&gt;</c>.
/// </summary>
/// <remarks>
/// The application form is unauthenticated, on purpose and permanently.
/// Applying is the first thing somebody does and there is nobody to be yet —
/// requiring an account first would mean a sign-up before the sign-up, and the
/// account it would demand is the one applying creates.
/// <para>
/// The code is therefore the whole permission for that form, which is why it
/// is seven random characters rather than a number. Nothing else is reachable
/// at all: no form list, no way to enumerate codes, and no way to read an
/// answer back.
/// </para>
/// <para>
/// A form can additionally require sign-in, and some must. A mentor sign-up,
/// an RSVP and a post-event survey are all answered by people we already have
/// on file, and asking them for an address again is both friction and a data
/// problem — they typo it, or use a different one, and the answer cannot be
/// joined to the application it is about. Those forms are for a named set of
/// applicant statuses, which is per-form configuration rather than a rule
/// here: an RSVP is for <c>accepted</c> and a feedback survey for
/// <c>checked_in</c>.
/// </para>
/// <para>
/// Two answers must never be confused and never be conflated: <em>not signed
/// in</em> is a state the page can act on by asking for an address, and
/// <em>signed in and not eligible</em> is a state where there is nothing to
/// do. Neither of them, and nothing on the sign-in step, may say whether a
/// given address is one we hold.
/// </para>
/// <para>
/// Nothing in this file logs an answer or an address. A submission is logged
/// as a form code and an id, which is enough to find the row and tells a log
/// reader nothing about who anybody is.
/// </para>
/// </remarks>
public static class PublicFormEndpoints
{
    public static IEndpointRouteBuilder MapForms(this IEndpointRouteBuilder app)
    {
        var forms = app.MapGroup("/forms");

        forms.MapGet("/{code}", GetForm);

        // The same limiter the portal's magic link is behind, because it is
        // the same hazard: an endpoint open to the internet that sends mail
        // from our domain to an address the caller chose. Unlimited, it makes
        // us a spam relay and destroys the sending reputation that login
        // depends on — and login is the one kind of mail that must not stop
        // arriving.
        //
        // Per IP here and per address inside the handler. Either alone is
        // trivially bypassed: one address from many hosts, or many addresses
        // from one host.
        forms.MapPost("/{code}/sign-in", RequestFormLink).RequireRateLimiting("magic-link");

        // Only the writes are throttled. Reading a form is what happens when
        // fifty people open the same link at the start of a club meeting, and
        // throttling that is throttling the event.
        forms.MapPost("/{code}/submit", Submit).RequireRateLimiting("form-submit");

        forms.MapPost("/{code}/resume", UploadResume)
             .RequireRateLimiting("resume-upload")

             // A second cap, held by the server before this handler is
             // reached. The check inside it is the one that produces a
             // sentence somebody can act on; this one is what stops a caller
             // streaming a gigabyte at us to find out we would have refused it.
             .WithMetadata(new RequestSizeLimitAttribute(MaxUploadRequestBytes));

        return app;
    }

    /// <summary>
    /// The whole request, envelope included.
    /// </summary>
    /// <remarks>
    /// A little over the file cap rather than exactly it: multipart wraps the
    /// bytes in boundaries and headers, so a file of exactly 5 MB arrives as
    /// slightly more than 5 MB and a limit set to the file size would refuse
    /// the largest file we say we accept.
    /// </remarks>
    private const int MaxUploadRequestBytes = ResumeFile.MaxBytes + (64 * 1024);

    /// <summary>
    /// What the page was sent back.
    /// </summary>
    /// <remarks>
    /// Bound as a nullable body for the same reason the admin endpoints are:
    /// minimal APIs bind before anything else runs, so a required body turns a
    /// malformed request into a 400 decided before this handler can answer it
    /// in its own words.
    /// </remarks>
    public sealed record SubmitRequest(Dictionary<string, JsonElement>? Answers);

    /// <summary>
    /// The published version of a form, or nothing.
    /// </summary>
    /// <remarks>
    /// A form whose only version is a draft answers 404, the same as a code
    /// nobody ever issued. From outside they are the same thing: an
    /// unpublished form is not a form anybody can fill in, and saying "this
    /// exists but is not ready" would tell a stranger a form is coming.
    /// <para>
    /// A closed form is different and answers 200. Somebody following a link
    /// off a flyer in March needs to be told the deadline passed, not shown a
    /// page that reads as a broken link they will report.
    /// </para>
    /// </remarks>
    /// <param name="respondents">
    /// Only consulted for a form that requires sign-in. An ungated form does
    /// not read the session at all, so the application form costs exactly what
    /// it did before.
    /// </param>
    private static async Task<IResult> GetForm(
        string code,
        HttpContext http,
        IFormStore forms,
        IRespondentStore respondents,
        SessionService sessions,
        TimeProvider clock,
        CancellationToken ct)
    {
        var form = await forms.ByCodeAsync(code, ct);
        if (form is null)
        {
            return NoSuchForm();
        }

        var published = await forms.PublishedAsync(form.Id, ct);
        if (published is null)
        {
            return NoSuchForm();
        }

        if (!form.IsOpen(clock.GetUtcNow()))
        {
            // No questions with it. There is nothing to fill in, and sending
            // the form down anyway invites a page that renders it behind a
            // banner somebody can scroll past.
            //
            // Checked ahead of the gate on purpose: a closed form is closed to
            // everybody, and asking somebody to sign in before telling them
            // the deadline passed is a round trip that ends in the same place.
            return Results.Ok(new
            {
                code = form.Code,
                name = form.Name,
                kind = form.Kind,
                open = false,
                closesAt = form.ClosesAt,
                requiresSignIn = form.IsGated,
                access = Closed,
            });
        }

        if (!form.IsGated)
        {
            return Results.Ok(new
            {
                code = form.Code,
                name = form.Name,
                kind = form.Kind,
                open = true,
                closesAt = form.ClosesAt,
                requiresSignIn = false,
                access = Open,
                version = published.Version,
                fields = published.Fields.Select(Public),
            });
        }

        var respondent = await WhoAsync(http, sessions, respondents, form, ct);

        // The two refusals, told apart. They are different states of the same
        // page: one has something to do — put in an address and wait for a
        // link — and the other has nothing, and offering a sign-in box to
        // somebody already signed in is how a person ends up requesting four
        // links to a form that will not open for them either way.
        //
        // Neither carries the questions. An ineligible reader is not somebody
        // to show the form to behind a banner.
        if (respondent is null || !form.Admits(respondent.Status))
        {
            return Results.Ok(new
            {
                code = form.Code,
                name = form.Name,
                kind = form.Kind,
                open = true,
                closesAt = form.ClosesAt,
                requiresSignIn = true,
                access = respondent is null ? SignIn : Ineligible,
            });
        }

        var locked = FixedAnswers.For(respondent);

        return Results.Ok(new
        {
            code = form.Code,
            name = form.Name,
            kind = form.Kind,
            open = true,
            closesAt = form.ClosesAt,
            requiresSignIn = true,
            access = Open,
            version = published.Version,
            fields = published.Fields.Select(Public),

            // Who they are, from the record rather than from a question. This
            // is what the sign-in was for: the page prints it and never asks
            // for it, so there is no address to typo and nothing to join on
            // afterwards.
            //
            // The status is deliberately absent. An applicant is never shown
            // their internal status — the portal goes to some trouble over
            // that, so that a reviewer can decide on Tuesday and the team
            // announce on Friday — and a form leaking one would undo it for
            // the same person on a different page.
            you = new { name = respondent.Name, email = respondent.Email },

            // Only what this form actually asks. Sending everything we hold
            // would hand a page the applicant's whole record because it
            // happened to ask one question.
            prefill = published.Fields
                .Where(field => field.Type != FieldType.Section
                                && respondent.Known.ContainsKey(field.Key))
                .ToDictionary(field => field.Key, field => respondent.Known[field.Key]),

            // Shown as fixed rather than hidden. A question that vanishes is
            // one somebody assumes was never asked; a question shown with its
            // answer and no control says what we hold and that this is not the
            // place to change it. See FixedAnswers for which and why.
            @fixed = published.Fields
                .Where(field => locked.Contains(field.Key))
                .Select(field => field.Key),
        });
    }

    /// <summary>
    /// What state the page is in, as one word.
    /// </summary>
    /// <remarks>
    /// A discriminator rather than a status code, because none of these is an
    /// error and every one of them is a page with something on it. A 401 for
    /// "not signed in" would be answered by the browser's own machinery and by
    /// portalforms' error boundary, neither of which can render an email box.
    /// </remarks>
    private const string Open = "open";
    private const string Closed = "closed";
    private const string SignIn = "signIn";
    private const string Ineligible = "ineligible";

    /// <summary>
    /// The person behind the session cookie, as this form's audience needs
    /// them. Null when there is no session, the session is dead, or they have
    /// no application on this form's event.
    /// </summary>
    /// <remarks>
    /// All three collapse to null on purpose. From the page's point of view
    /// they are one state — "we do not know who you are, ask for an address" —
    /// and telling them apart would answer "does this address have an
    /// application" for anybody who could get a session at all.
    /// <para>
    /// The session is revalidated against the database like every other gate
    /// in this codebase, so revoking one closes a form on the next request
    /// rather than at expiry.
    /// </para>
    /// </remarks>
    private static async Task<Respondent?> WhoAsync(
        HttpContext http,
        SessionService sessions,
        IRespondentStore respondents,
        Form form,
        CancellationToken ct)
    {
        var token = http.Request.Cookies[RequirePermissionExtensions.SessionCookie];
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var session = await sessions.ValidateAsync(token, ct);
        if (!session.Accepted)
        {
            return null;
        }

        return await respondents.ForPersonAsync(form.EventId, session.PersonId, form.Id, ct);
    }

    public sealed record SignInRequest(string? Email);

    /// <summary>
    /// Sends a link that opens this form, if the address is one we hold.
    /// </summary>
    /// <remarks>
    /// Answers identically whether or not the address is on file, in status,
    /// body and wording — exactly as <c>/auth/magic-link</c> does, and for the
    /// same reason. A different answer for a known address turns a form
    /// anybody can open into a way to ask who applied to the hackathon, and
    /// the form's own link is handed out on flyers.
    /// <para>
    /// Timing is close but not constant: an address we hold costs a lookup, an
    /// upsert and a queued row. Closing that gap properly means doing the work
    /// regardless, which means sending mail to strangers; until then the rate
    /// limiter is what makes the difference impractical to measure. Same trade
    /// as the portal's, written down in the same place.
    /// </para>
    /// <para>
    /// The account is created here when there is not one yet, which is what
    /// makes this work for somebody who has never opened the portal.
    /// Registering does not create an account today, so most applicants are in
    /// <c>applications.applications</c> and not in <c>identity.people</c> —
    /// without this, a sign-in form would refuse everybody it was built for.
    /// What stops it being a way to fill that table is the check above it: the
    /// address has to already have an application on this form's own event.
    /// </para>
    /// </remarks>
    private static async Task<IResult> RequestFormLink(
        string code,
        SignInRequest? request,
        IFormStore forms,
        IRespondentStore respondents,
        IIdentityStore people,
        MagicLinkService links,
        IEmailSender email,
        IConfiguration config,
        IMemoryCache cache,
        ILogger<SignInRequest> log,
        CancellationToken ct)
    {
        var form = await forms.ByCodeAsync(code, ct);
        if (form is null || await forms.PublishedAsync(form.Id, ct) is null)
        {
            return NoSuchForm();
        }

        if (!form.IsGated)
        {
            // Not a refusal about the address, so it does not have to be
            // careful about one. A form that anybody can open has no sign-in
            // step to attempt, and saying so is the only answer that leaves
            // the caller anywhere to go.
            return Results.BadRequest(new { error = "This form does not use sign-in." });
        }

        if (string.IsNullOrWhiteSpace(request?.Email))
        {
            return Results.BadRequest(new { error = "An email address is required." });
        }

        // Before any database work, like the portal's. Rejection is answered
        // exactly like success: "too many requests for this address" would
        // confirm the address exists, which is what the identical response is
        // there to hide.
        if (AuthEndpoints.TooManyFor(cache, request.Email, "form-sign-in"))
        {
            return Sent();
        }

        var onFile = await respondents.FindOnFileAsync(form.EventId, request.Email, ct);
        if (onFile is null)
        {
            // Logged without the address. That somebody tried an address we do
            // not hold is worth knowing; which address is not worth storing.
            log.LogInformation(
                "A form sign-in was requested for an address that is not on file. {code}",
                form.Code);

            return Sent();
        }

        // The stored address, not the one that was typed. They differ in case
        // often enough — a phone capitalises the first letter — and the
        // account, the link and the row should all agree on one string.
        var personId = await people.EnsureHackerAsync(onFile.Email, onFile.FullName, ct);
        if (personId is null)
        {
            // An organizer's address, or somebody revoked. Answered like
            // everything else here, and not mailed: organizers sign in through
            // Google so their access is tied to an allowlisted account, and a
            // hacker link would be a second way in that skips it.
            return Sent();
        }

        if (onFile.PersonId is null)
        {
            // The application had no account against it until now. Linking it
            // is what lets the form find them again after the link is clicked,
            // and what makes the answer joinable to the application.
            await respondents.LinkPersonAsync(onFile.ApplicationId, personId.Value, ct);
        }

        var issued = await links.IssueAsync(onFile.Email, ct);
        if (issued is not null)
        {
            await email.SendMagicLinkAsync(
                issued.PersonId,
                onFile.Email,
                AuthEndpoints.FormLink(config, form.Code, issued.Token),
                ct);
        }

        return Sent();
    }

    /// <summary>
    /// The one answer the sign-in step ever gives.
    /// </summary>
    /// <remarks>
    /// Every path returns this: on file, not on file, an organizer's address,
    /// throttled. One method rather than the literal repeated, so a future
    /// edit cannot make one of them different by accident — and a difference
    /// is the whole failure.
    /// </remarks>
    private static IResult Sent() => Results.Accepted(value: new
    {
        message = "If that address is on file, a sign-in link is on its way.",
    });

    /// <summary>
    /// A question as an applicant is allowed to see it.
    /// </summary>
    /// <remarks>
    /// Storage and Column are deliberately absent. Where an answer is kept is
    /// this side's business, and publishing the column names of the
    /// applications table to an unauthenticated page is free information for
    /// somebody probing it.
    /// </remarks>
    private static object Public(FormField field) => new
    {
        key = field.Key,

        // Spelled the way the stored form spells it, so the page switches on
        // the same string the builder wrote.
        type = JsonNamingPolicy.CamelCase.ConvertName(field.Type.ToString()),
        label = field.Label,
        help = field.Help,
        required = field.Required,
        options = field.Options.Select(o => new { value = o.Value, label = o.Label }),
        minLength = field.MinLength,
        maxLength = field.MaxLength,
        min = field.Min,
        max = field.Max,
    };

    /// <summary>
    /// Takes a completed form.
    /// </summary>
    /// <remarks>
    /// The questions are loaded here and never read from the request. The
    /// browser is told what to render by the same published version, but a
    /// caller can send anything at all — so a field list that arrived with the
    /// answers would be a claim validating itself.
    /// </remarks>
    private static async Task<IResult> Submit(
        string code,
        SubmitRequest? request,
        HttpContext http,
        IFormStore forms,
        ISubmissionStore submissions,
        IRespondentStore respondents,
        SessionService sessions,
        TimeProvider clock,
        ILogger<SubmitRequest> log,
        CancellationToken ct)
    {
        var form = await forms.ByCodeAsync(code, ct);
        if (form is null)
        {
            return NoSuchForm();
        }

        var published = await forms.PublishedAsync(form.Id, ct);
        if (published is null)
        {
            return NoSuchForm();
        }

        if (!form.IsOpen(clock.GetUtcNow()))
        {
            // 410 rather than 404: the form was here and is not accepting any
            // more. That distinction is the difference between "check your
            // link" and "you missed the deadline".
            return Results.Json(
                new { error = "This form has closed." },
                statusCode: StatusCodes.Status410Gone);
        }

        if (form.IsGated)
        {
            return await SubmitSignedIn(
                form, published, request, http, respondents, sessions, log, ct);
        }

        if (!form.IsApplication)
        {
            // Survey answers have nowhere to go yet. Answering 200 and
            // dropping them would be the worst of the options: somebody would
            // believe they had replied.
            log.LogWarning(
                "A submission arrived for a form that has nowhere to store answers. {code}",
                form.Code);

            return Results.Json(
                new { error = "This form is not accepting responses yet." },
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var answers = request?.Answers
                      ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        var problems = SubmissionValidation.Check(published.Fields, answers);
        if (problems.Count > 0)
        {
            // Every problem at once, keyed by field, so the page can put each
            // one against the question it belongs to rather than stacking
            // sentences at the top.
            return Results.BadRequest(new
            {
                error = "Some answers need another look.",
                problems = problems.Select(p => new { field = p.FieldKey, message = p.Message }),
            });
        }

        try
        {
            var id = await submissions.SubmitApplicationAsync(form, published, answers, ct);

            log.LogInformation(
                "Application submitted. {code} {applicationId} {event}",
                form.Code, id, Events.ApplicationSubmitted);

            return Results.Ok(new { submitted = true });
        }
        catch (DuplicateApplicationException)
        {
            // The unique index, surfaced as a sentence. This is the common
            // case of somebody applying twice rather than an attack, and the
            // message has to leave them somewhere to go.
            return Results.Conflict(new
            {
                error = "An application already exists for that email address. "
                        + "Check your inbox for the one you sent.",
            });
        }
        catch (ResumeUploadNotClaimableException)
        {
            // Keyed to the file question, so the message lands beside the
            // control that has to be used again rather than as a banner at the
            // top of a form somebody has already scrolled past.
            var file = published.Fields.FirstOrDefault(f => f.Type == FieldType.File);

            return Results.BadRequest(new
            {
                error = "That file needs uploading again.",
                problems = new[]
                {
                    new
                    {
                        field = file?.Key,
                        message = "We no longer have that upload. Pick your resume again.",
                    },
                },
            });
        }
        catch (FormCannotCreateApplicantsException)
        {
            // Nothing the applicant did. Logged loudly with the code, because
            // the fix is republishing the form with an email question on it.
            log.LogError(
                "An application form has no question that can hold an address. {code}",
                form.Code);

            return Results.Json(
                new { error = "This form cannot accept applications. Let the organizers know." },
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Takes an answer from somebody who signed in to give it.
    /// </summary>
    /// <remarks>
    /// The answer is recorded against the person, never against an address
    /// that arrived with the request. That is the whole difference between
    /// this and the application form's submit: there is no address to typo,
    /// nothing to deduplicate afterwards by hand, and the row joins to the
    /// application without anybody matching strings.
    /// <para>
    /// Eligibility is checked here as well as on the read. They are two
    /// requests with a gap between them, and the gap is wide enough for a
    /// decision to land — somebody who opened an RSVP as <c>accepted</c> and
    /// submits it after being withdrawn must not be writing to it.
    /// </para>
    /// </remarks>
    private static async Task<IResult> SubmitSignedIn(
        Form form,
        FormVersion published,
        SubmitRequest? request,
        HttpContext http,
        IRespondentStore respondents,
        SessionService sessions,
        ILogger<SubmitRequest> log,
        CancellationToken ct)
    {
        var respondent = await WhoAsync(http, sessions, respondents, form, ct);

        if (respondent is null)
        {
            // 401 rather than the read's quiet "sign in" state, because this
            // is a write that did not happen and the page has to know the
            // difference. It says nothing about whether an address is on file:
            // the caller either has a live session for somebody with an
            // application here, or they do not.
            return Results.Json(
                new { error = "Sign in to answer this form." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!form.Admits(respondent.Status))
        {
            // Which status they are in is not said, and neither is which
            // statuses the form wants. They are signed in, so this is a
            // refusal rather than a hidden route — but an applicant is never
            // told their internal status, and a form is not the place that
            // rule stops applying.
            return Results.Json(
                new { error = "This form is not open to you." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Before validation, not after. A required fixed question posted empty
        // would otherwise be refused for being unanswered while we are holding
        // the answer — and a crafted one would be validated as though the
        // caller's value counted.
        var answers = FixedAnswers.Apply(
            request?.Answers ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            published.Fields,
            respondent);

        var problems = SubmissionValidation.Check(published.Fields, answers);
        if (problems.Count > 0)
        {
            return Results.BadRequest(new
            {
                error = "Some answers need another look.",
                problems = problems.Select(p => new { field = p.FieldKey, message = p.Message }),
            });
        }

        var id = await respondents.RecordAsync(
            form.Id, published.Version, respondent, answers, ct);

        // The code and the ids. Not the person's address and not a single
        // answer, like every other line this file writes.
        log.LogInformation(
            "A signed-in form was answered. {code} {submissionId} {PersonId}",
            form.Code, id, respondent.PersonId);

        return Results.Ok(new { submitted = true });
    }

    /// <summary>
    /// Takes the resume, before the rest of the form is finished.
    /// </summary>
    /// <remarks>
    /// Separate from the submit because of when it happens rather than because
    /// of what it is. Somebody picks a file half way down a thirty-question
    /// form, and the bytes should be moving while they finish the rest of it —
    /// on campus wifi, five megabytes at the end of a form is a progress bar
    /// somebody watches instead of a submission that lands.
    /// <para>
    /// What comes back is the id of a row we wrote, not the key we wrote it
    /// under. That is the whole reason this endpoint has a shape at all: the
    /// page repeats the id at submit, and an id can be checked against
    /// something we issued, where a key would be a caller naming a blob.
    /// </para>
    /// <para>
    /// Three things decide whether this is safe, and all three are here rather
    /// than in the browser: the bytes have to start <c>%PDF-</c>, they have to
    /// be under five megabytes measured as they are read, and the key they are
    /// written under is generated. Nothing about the uploaded filename is
    /// trusted, and nothing about it is logged.
    /// </para>
    /// </remarks>
    private static async Task<IResult> UploadResume(
        string code,
        HttpRequest request,
        IFormStore forms,
        ISubmissionStore submissions,
        IResumeStore resumes,
        TimeProvider clock,
        ILogger<SubmitRequest> log,
        CancellationToken ct)
    {
        var form = await forms.ByCodeAsync(code, ct);
        if (form is null)
        {
            return NoSuchForm();
        }

        // Checked before a byte is read. A closed form is not a place to leave
        // files, and an unpublished one has no question to attach them to.
        var published = await forms.PublishedAsync(form.Id, ct);
        if (published is null)
        {
            return NoSuchForm();
        }

        if (!form.IsOpen(clock.GetUtcNow()))
        {
            return Results.Json(
                new { error = "This form has closed." },
                statusCode: StatusCodes.Status410Gone);
        }

        // A form with no file question has nowhere to put a resume, so this is
        // a 404 for the same reason an unknown code is: nothing here accepts
        // one.
        if (!published.Fields.Any(f => f.Type == FieldType.File))
        {
            return NoSuchForm();
        }

        if (!resumes.Available)
        {
            // Configuration, not the applicant. Answered as an outage so the
            // page can say "try again shortly" rather than telling somebody
            // their perfectly good PDF was refused.
            log.LogError(
                "A resume was uploaded and there is no object store configured. {code}",
                form.Code);

            return Results.Json(
                new { error = "Uploads are unavailable right now. Try again shortly." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var picked = await ReadTheFileAsync(request, ct);
        if (picked is null)
        {
            return Results.BadRequest(new { error = "No file arrived. Pick it again." });
        }

        var (rawName, content) = picked.Value;
        if (content is null)
        {
            return Refused(ResumeRejection.TooLarge);
        }

        var rejection = ResumeFile.Inspect(content);
        if (rejection != ResumeRejection.None)
        {
            return Refused(rejection);
        }

        var key = await resumes.StoreAsync(form.EventId, content, ct);

        var filename = ResumeFile.TidyFilename(rawName);
        var upload = await submissions.RecordResumeAsync(
            form.Id, key, filename, content.Length, ct);

        // The code, the id and the size. Not the name and not the key: one is
        // usually somebody's own name and the other is where their CV lives.
        log.LogInformation(
            "A resume was stored. {code} {uploadId} {bytes} {event}",
            form.Code, upload, content.Length, Events.ResumeStored);

        // The name goes back so the page can show what it is holding. It came
        // from the caller, so this is repeating their own word to them rather
        // than telling them anything.
        return Results.Ok(new { upload, name = filename, size = content.Length });
    }

    /// <summary>
    /// Pulls the one file out of a multipart body, without letting it land
    /// anywhere first.
    /// </summary>
    /// <remarks>
    /// Read as a stream rather than through <c>ReadFormAsync</c>, and the
    /// difference is what the cap actually means. Form binding buffers
    /// anything over 64 KB to a temporary file and then hands it over, so
    /// every check would be running against bytes already written to disk;
    /// this way a file over the limit is abandoned mid-flight and never exists
    /// anywhere.
    /// <para>
    /// Null means there was no file part at all. A null <c>Content</c> inside
    /// the result means there was one and it went past the cap.
    /// </para>
    /// </remarks>
    private static async Task<(string? Name, byte[]? Content)?> ReadTheFileAsync(
        HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType
            || !MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType))
        {
            return null;
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary);
        if (StringSegment.IsNullOrEmpty(boundary))
        {
            return null;
        }

        var reader = new MultipartReader(boundary.Value!, request.Body);

        // The first file part wins and the rest of the body is not read. The
        // form asks one file question — FormValidation refuses to publish a
        // form with two — so a second attachment is either a mistake or
        // somebody seeing what happens.
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(
                    section.ContentDisposition, out var disposition)
                || !disposition.IsFileDisposition())
            {
                continue;
            }

            var name = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue
                ? disposition.FileNameStar
                : disposition.FileName);

            return (name.Value, await ReadAtMostAsync(section.Body, ResumeFile.MaxBytes, ct));
        }

        return null;
    }

    /// <summary>
    /// Reads a stream, and gives up rather than growing past a limit.
    /// </summary>
    /// <remarks>
    /// One byte past the cap is enough to know: it is read so that a file of
    /// exactly five megabytes is kept and the one after it is refused, without
    /// having to trust a length anybody sent.
    /// </remarks>
    private static async Task<byte[]?> ReadAtMostAsync(
        Stream source, int limit, CancellationToken ct)
    {
        var buffer = new byte[limit + 1];
        var read = 0;

        while (read < buffer.Length)
        {
            var got = await source.ReadAsync(buffer.AsMemory(read), ct);
            if (got == 0)
            {
                break;
            }

            read += got;
        }

        return read > limit ? null : buffer[..read];
    }

    /// <summary>A refused upload, in words that say what to do next.</summary>
    /// <remarks>
    /// 400 rather than 413 even for the oversized case. The page reads the
    /// sentence out of the body and puts it against the question, and a status
    /// code that means "the request was too big" is one more branch there for
    /// no benefit to the person reading the screen.
    /// </remarks>
    private static IResult Refused(ResumeRejection rejection) =>
        Results.BadRequest(new { error = ResumeFile.Explain(rejection) });

    /// <summary>
    /// One answer for a code that does not exist, is not published, or was
    /// mistyped.
    /// </summary>
    /// <remarks>
    /// Identical in every case on purpose. Distinguishing them would turn the
    /// endpoint into a way to find out which seven-character codes are real.
    /// </remarks>
    private static IResult NoSuchForm() =>
        Results.NotFound(new { error = "No form with that code." });
}
