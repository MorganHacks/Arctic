using Microsoft.AspNetCore.Mvc;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// The applicant's own resume: what we are holding, and replacing it.
/// </summary>
/// <remarks>
/// Its own file rather than more handlers in <see cref="PortalEndpoints"/>,
/// which is already long and is edited by everybody. The routes are still
/// registered inside <c>MapPortal</c> so they inherit the one group that
/// carries the feature flag and the session gate — a second
/// <c>MapGroup("/portal")</c> here would be two places for those to be
/// configured and one place for them to be forgotten.
/// <para>
/// Everything <see cref="PortalEndpoints"/> says about safety applies here
/// unchanged, and the important half of it is worth repeating: <b>no route in
/// this file takes an application id, a person id or a storage key from the
/// caller.</b> The person comes from the session and the row comes from
/// <see cref="IApplicantPortalStore"/>, whose every statement narrows on it.
/// Writing another applicant's resume is not a check somebody could forget to
/// write — there is no parameter through which the attempt could be expressed.
/// </para>
/// <para>
/// <b>Why this exists at all.</b> A resume was previously something you could
/// only attach while filling in the application form, and only ever once. That
/// is the wrong shape for the thing it is for: sponsors read these at and after
/// the event, months after somebody uploaded a file in a hurry on a phone. The
/// alternatives to this endpoint are an applicant emailing a PDF to an
/// organizer — which puts a resume in an inbox instead of in the private
/// container built for it — or nobody updating anything, which is what was
/// happening.
/// </para>
/// <para>
/// <b>Why there is no download here.</b> The organizers' side hands back a
/// signed link because a reviewer has to read a file they have never seen. An
/// applicant is the person who uploaded it. Minting a link for them buys
/// nothing and costs five minutes in which a resume URL exists, so this file
/// answers "we have this" with a name, a size and a date, and never with a way
/// to fetch the bytes.
/// </para>
/// <para>
/// Nothing here logs a filename, an address or a key. See
/// <c>Redaction.SensitiveKeys</c>.
/// </para>
/// </remarks>
public static class PortalResumeEndpoints
{
    /// <summary>
    /// Hangs the two resume routes off the portal group.
    /// </summary>
    /// <remarks>
    /// Takes the already-configured group rather than the application, so the
    /// session gate and the feature flag cannot be missing from these two
    /// routes while being present on every other one.
    /// </remarks>
    internal static RouteGroupBuilder MapPortalResume(this RouteGroupBuilder portal)
    {
        portal.MapGet("/resume", GetResume);

        portal.MapPost("/resume", ReplaceResume)

              // The same limiter the public form's upload is behind. This one
              // additionally requires a session, so it is far harder to abuse,
              // but the hazard it guards is the same: a route that writes
              // megabytes into a storage account on request.
              .RequireRateLimiting("resume-upload")

              // A second cap, held by the server before the handler is
              // reached. The check inside it produces a sentence somebody can
              // act on; this one is what stops a caller streaming a gigabyte
              // at us to find out we would have refused it.
              .WithMetadata(new RequestSizeLimitAttribute(MaxUploadRequestBytes));

        return portal;
    }

    /// <summary>
    /// The whole request, envelope included.
    /// </summary>
    /// <remarks>
    /// A little over the file cap rather than exactly it: multipart wraps the
    /// bytes in boundaries and headers, so a file of exactly 5 MB arrives as
    /// slightly more than 5 MB and a limit set to the file size would refuse
    /// the largest file we say we accept. The same number the public form
    /// uses, derived the same way rather than copied as a literal.
    /// </remarks>
    private const int MaxUploadRequestBytes = ResumeFile.MaxBytes + (64 * 1024);

    /// <summary>
    /// What resume, if any, is on their application.
    /// </summary>
    /// <remarks>
    /// Its own route rather than a field on <c>/portal/me</c>, which is read
    /// by every screen in the portal on every load. Only one screen needs
    /// this, and putting it on the shared projection would mean an extra
    /// column read for every status page anybody opens.
    /// <para>
    /// Answers 200 in every case, including for somebody with no application
    /// and somebody with no resume. There is always a screen; what changes is
    /// whether it has a file on it and whether the picker is enabled. A 404
    /// would only have to be translated back into these same sentences by the
    /// page.
    /// </para>
    /// </remarks>
    private static async Task<IResult> GetResume(
        HttpContext http, IApplicantPortalStore store, CancellationToken ct)
    {
        var personId = http.PersonId();
        var application = await store.FindForPersonAsync(personId, ct);

        // Not asked for at all when there is no application, so the empty case
        // costs one query rather than two.
        var resume = application is null
            ? null
            : await store.ResumeForPersonAsync(personId, ct);

        return Results.Ok(new
        {
            // Whether there is an application at all, said outright rather than
            // left to be inferred from `editable` being false with no
            // `lockedReason`. That inference happens to hold today and would
            // stop holding the first time somebody adds a status with no
            // sentence, and the screen it breaks is the one somebody sees
            // before they have applied.
            started = application is not null,

            // Null both for "no application" and "no file yet". The page shows
            // the same empty state for both and the sentence explaining which
            // one it is comes from `editable` and `lockedReason` below.
            resume = resume is null ? null : new
            {
                // Text the applicant typed on their own machine, going back to
                // the applicant who typed it. It is rendered as content on a
                // page and reaches no header, no path and no log line.
                filename = resume.Filename,
                size = resume.Size,
                uploadedAt = resume.UploadedAt,
            },

            // The same rule the write is judged by, so the screen cannot offer
            // a picker the endpoint would refuse — nor withhold one it would
            // accept, which is the failure nobody notices.
            editable = application is not null && ResumeEditing.IsOpen(application.Status),

            // Non-null only when there is an application that has moved past
            // the point where the resume is theirs. "You have not applied yet"
            // is a different sentence and belongs to the page, which already
            // knows that from /portal/me.
            lockedReason = application is null
                ? null
                : ResumeEditing.WhyClosed(application.Status),

            // The limits, said by the side that enforces them. The page checks
            // both before it uploads as a courtesy, and a page that carried its
            // own copy of these numbers is a page that eventually disagrees
            // with the API about what will be accepted.
            maxBytes = ResumeFile.MaxBytes,
            accepts = ResumeFile.ContentType,
        });
    }

    /// <summary>
    /// Replaces the resume on their own application.
    /// </summary>
    /// <remarks>
    /// Upload and replace are one route because to an applicant they are one
    /// action — there is exactly one resume on an application, and picking a
    /// file means "use this one" whether or not something was there before. A
    /// separate PUT would be a distinction only the database can see.
    /// <para>
    /// The order of the checks is the security story, and it is deliberate:
    /// </para>
    /// <list type="number">
    /// <item>
    /// Whether the resume is still theirs to change, read <i>before</i> a byte
    /// is taken off the wire. Somebody whose application is closed should not
    /// be able to make us hold five megabytes to be told no.
    /// </item>
    /// <item>
    /// Whether the store is configured, so a missing storage account is an
    /// outage rather than a refused PDF.
    /// </item>
    /// <item>
    /// The bytes, capped as they are read and inspected before any of them are
    /// written anywhere. The content decides what the file is; the part's
    /// declared <c>Content-Type</c> and the <c>.pdf</c> on the end of the name
    /// are both claims by whoever uploaded it and neither is consulted.
    /// </item>
    /// <item>
    /// The write, which re-tests the status inside the same statement. The
    /// check in step one is what produces the right sentence; this is the one
    /// that is actually load-bearing, because an organizer can decide the
    /// application while the upload is in flight.
    /// </item>
    /// </list>
    /// <para>
    /// <b>The previous file is left in the container.</b> There is no delete on
    /// <see cref="IResumeStore"/> and this endpoint deliberately does not add
    /// one: an API that can remove resumes is a much worse thing to be holding
    /// than a few orphaned blobs, and a bug in the wrong place would be
    /// unrecoverable rather than expensive. The orphans are the same storage
    /// bill as the unclaimed uploads from the public form — see
    /// <c>0013_resume_uploads.sql</c>, which describes the sweeper neither of
    /// them has yet.
    /// </para>
    /// <para>
    /// The same is true of the narrow window between the object store write and
    /// the database write. If the row update loses its race, the bytes are
    /// already stored and nothing points at them — an orphan, not a lost
    /// resume, and the applicant is told plainly that the save did not happen.
    /// Writing the row first would be the other way round, which is an
    /// application pointing at a blob that does not exist.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ReplaceResume(
        HttpRequest request,
        HttpContext http,
        IApplicantPortalStore store,
        IResumeStore resumes,
        ILogger<ApplicantResume> log,
        CancellationToken ct)
    {
        var personId = http.PersonId();
        var application = await store.FindForPersonAsync(personId, ct);

        if (application is null)
        {
            // COPY: needs sign-off.
            return Results.Conflict(new
            {
                error = "You have not started an application yet.",
            });
        }

        if (!ResumeEditing.IsOpen(application.Status))
        {
            // The store would refuse this anyway. Checking here as well is what
            // turns the refusal into a sentence rather than a bare 409, and it
            // is what stops us reading a file we were never going to keep.
            return Results.Conflict(new
            {
                error = ResumeEditing.WhyClosed(application.Status),
            });
        }

        if (!resumes.Available)
        {
            // Configuration, not the applicant. Answered as an outage so the
            // page can say "try again shortly" rather than telling somebody
            // their perfectly good PDF was refused.
            log.LogError(
                "An applicant uploaded a resume and there is no object store "
                + "configured. {PersonId}",
                personId);

            // COPY: needs sign-off.
            return Results.Json(
                new { error = "Uploads are unavailable right now. Try again shortly." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var picked = await UploadedFile.ReadOneAsync(request, ResumeFile.MaxBytes, ct);
        if (picked is null)
        {
            // COPY: needs sign-off. Reworded from the public form's version of
            // the same message, which says "Pick it again" — this screen has a
            // Choose-file control rather than a form field.
            return Results.BadRequest(new { error = "No file arrived. Choose it again." });
        }

        var (rawName, content) = picked.Value;
        if (content is null)
        {
            // The part ran past the cap and was abandoned mid-flight, so this
            // is the oversize sentence rather than a generic failure.
            return Refused(ResumeRejection.TooLarge);
        }

        var rejection = ResumeFile.Inspect(content);
        if (rejection != ResumeRejection.None)
        {
            return Refused(rejection);
        }

        // The applicant's own event, read off the row we just found by their
        // person id. Not from the request: an event id a caller could name is
        // a caller choosing which year's folder to write into.
        var key = await resumes.StoreAsync(application.EventId, content, ct);

        var filename = ResumeFile.TidyFilename(rawName);
        var saved = await store.SaveResumeAsync(personId, key, filename, content.Length, ct);

        if (saved is not ResumeSave.Saved)
        {
            // Lost the race against a decision landing between the read above
            // and this write. Rare, and the honest answer is the same one the
            // check above gives.
            return Results.Conflict(new
            {
                error = saved is ResumeSave.NoApplication
                    // COPY: needs sign-off.
                    ? "You have not started an application yet."
                    : ResumeEditing.WhyClosed(application.Status)
                      // COPY: needs sign-off.
                      ?? "Your resume is locked.",
            });
        }

        // The person id and the size. Not the name — people call these
        // "Ada Lovelace CV.pdf" — and not the key, which is the one string that
        // says where somebody's CV lives.
        log.LogInformation(
            "An applicant replaced their resume. {PersonId} {bytes} {event}",
            personId, content.Length, Events.ResumeReplaced);

        // Re-read rather than assembled from what we just sent, so the screen
        // redraws from the same projection the GET serves and the timestamp is
        // the one the database wrote rather than a second opinion about when
        // the upload happened.
        var stored = await store.ResumeForPersonAsync(personId, ct);

        return Results.Ok(new
        {
            resume = stored is null ? null : new
            {
                filename = stored.Filename,
                size = stored.Size,
                uploadedAt = stored.UploadedAt,
            },
        });
    }

    /// <summary>A refused upload, in words that say what to do next.</summary>
    /// <remarks>
    /// The wording is <see cref="ResumeFile.Explain"/>'s rather than this
    /// file's, so the portal and the application form say the same thing about
    /// the same file. 400 rather than 413 even for the oversized case, matching
    /// the public form: the page reads the sentence out of the body and shows
    /// it, and a status meaning "the request was too big" is one more branch
    /// there for no benefit to the person reading the screen.
    /// </remarks>
    private static IResult Refused(ResumeRejection rejection) =>
        Results.BadRequest(new { error = ResumeFile.Explain(rejection) });
}
