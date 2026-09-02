using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Domain;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// How a reviewer reads a resume.
/// </summary>
/// <remarks>
/// Behind <c>applications.view_resume</c> and not <c>applications.view</c>.
/// The permission model splits them deliberately: reading the queue is a large
/// group, and a CV carries a home address, a phone number and a photograph
/// that the application form never asked for.
/// <para>
/// What comes back is a link that stops working in five minutes, not the file.
/// The alternative — streaming the bytes through this API — would put every
/// resume through a service sized for JSON, and would make the link somebody
/// pastes into a group chat a permanent one. This way the URL is the
/// permission and the URL expires.
/// </para>
/// </remarks>
public static class ResumeEndpoints
{
    public static IEndpointRouteBuilder MapResumes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/applications/{id:guid}/resume", GetResume)
           .RequirePermission(Permission.ApplicationsViewResume);

        return app;
    }

    /// <summary>
    /// A signed link to one application's resume. Requires
    /// <c>applications.view_resume</c>.
    /// </summary>
    /// <remarks>
    /// The link, when it expires, and the name the applicant used — which the
    /// review screen shows as text and never as part of a URL or a header.
    /// <para>
    /// JSON rather than a redirect. A redirect would work for a reviewer
    /// clicking a link and be useless to the screen that actually needs this,
    /// which embeds the file beside the answers and has to know when to ask
    /// for a fresh URL. Handing over the expiry lets it do that without
    /// discovering the problem as a broken frame.
    /// </para>
    /// </remarks>
    private static async Task<IResult> GetResume(
        Guid id,
        HttpContext http,
        IApplicationStore applications,
        IResumeStore resumes,
        ILogger<StoredResume> log,
        CancellationToken ct)
    {
        var stored = await applications.ResumeOfAsync(id, ct);
        if (stored is null)
        {
            // Covers an application with no resume and an id that is not an
            // application. The caller already holds the permission to read
            // these, so there is nothing being hidden — it is one answer
            // instead of two for the same "there is nothing to show".
            return Results.NotFound(new { error = "No resume on that application." });
        }

        if (!resumes.Available)
        {
            log.LogError(
                "A resume was asked for and there is no object store configured. {applicationId}",
                id);

            return Results.Json(
                new { error = "Resumes are unavailable right now." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        SignedResume link;
        try
        {
            link = await resumes.LinkToAsync(
                stored.StorageKey, ResumeFile.DownloadName(id), ct);
        }
        catch (ResumeMissingException)
        {
            // The row says there are bytes and the store disagrees. Loud,
            // because it means an object went missing rather than that a
            // reviewer asked for something reasonable and got no.
            log.LogError(
                "An application points at a resume the store does not have. {applicationId}",
                id);

            return Results.NotFound(new { error = "That resume could not be found." });
        }

        // Who read whose, and when. The permission model calls a resume more
        // sensitive than the rest of an application, which is only true if
        // reading one leaves a record. The filename is not in it.
        log.LogInformation(
            "A resume was read. {actor} {applicationId} {event}",
            http.PersonId(), id, Events.ResumeRead);

        return Results.Ok(new
        {
            url = link.Url,
            expiresAt = link.ExpiresAt,

            // For the screen to show, not for a URL or a header. It is text a
            // stranger uploaded, and the only safe place for it is inside the
            // page as content.
            filename = stored.Filename,
            size = stored.Size,
        });
    }
}
