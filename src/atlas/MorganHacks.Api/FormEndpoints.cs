using System.Text.Json;
using MorganHacks.Applications.Forms;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// The public face of a form: <c>forms.morganhacks.com/&lt;code&gt;</c>.
/// </summary>
/// <remarks>
/// Unauthenticated, on purpose. Applying is the first thing somebody does and
/// there is nobody to be yet — requiring an account first would mean a sign-up
/// before the sign-up.
/// <para>
/// The code is therefore the whole permission, which is why it is seven random
/// characters rather than a number. Everything here is reachable by anyone
/// holding the link and nothing else is reachable at all: no form list, no way
/// to enumerate codes, and no way to read an answer back.
/// </para>
/// <para>
/// Nothing in this file logs an answer or an address. A submission is logged
/// as a form code and an application id, which is enough to find the row and
/// tells a log reader nothing about who anybody is.
/// </para>
/// </remarks>
public static class FormEndpoints
{
    public static IEndpointRouteBuilder MapForms(this IEndpointRouteBuilder app)
    {
        var forms = app.MapGroup("/forms");

        forms.MapGet("/{code}", GetForm);

        // Only the write is throttled. Reading a form is what happens when
        // fifty people open the same link at the start of a club meeting, and
        // throttling that is throttling the event.
        forms.MapPost("/{code}/submit", Submit).RequireRateLimiting("form-submit");

        return app;
    }

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
    private static async Task<IResult> GetForm(
        string code,
        IFormStore forms,
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
            return Results.Ok(new
            {
                code = form.Code,
                name = form.Name,
                kind = form.Kind,
                open = false,
                closesAt = form.ClosesAt,
            });
        }

        return Results.Ok(new
        {
            code = form.Code,
            name = form.Name,
            kind = form.Kind,
            open = true,
            closesAt = form.ClosesAt,
            version = published.Version,
            fields = published.Fields.Select(Public),
        });
    }

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
        IFormStore forms,
        ISubmissionStore submissions,
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
