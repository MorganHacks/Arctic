using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Services;
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
public static class PublicFormEndpoints
{
    public static IEndpointRouteBuilder MapForms(this IEndpointRouteBuilder app)
    {
        var forms = app.MapGroup("/forms");

        forms.MapGet("/{code}", GetForm);

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
