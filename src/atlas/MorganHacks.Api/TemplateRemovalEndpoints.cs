using MorganHacks.Lark.Data.Data;
using MorganHacks.Observability;

namespace MorganHacks.Api;

public static partial class TemplateEndpoints
{
    private static async Task<IResult> Remove(
        string key, int version, HttpContext http, TemplateCatalog templates,
        TemplateDraftStore drafts, ILogger<TemplateRequest> log, CancellationToken ct)
    {
        if (version < 0)
            return Results.BadRequest(new { error = "A valid template version is required." });

        if (key is QueuedEmailSender.TemplateKey or QueuedEmailSender.WelcomeTemplateKey)
            return Results.Conflict(new { error = "This template is required for account access and cannot be deleted." });

        var removed = version == 0
            ? await drafts.DeleteUnpublishedAsync(key, http.PersonId(), ct)
            : await templates.RetireAsync(key, version, ct);

        if (!removed)
        {
            if (await templates.FindAsync(key, ct) is not null)
                return Results.Conflict(new { error = "This template has changed. Refresh the gallery before deleting it." });
            return Results.NotFound(new { error = NoSuchTemplate });
        }

        log.LogInformation("A template was removed. {actor} {template} {version} {event}",
            http.PersonId(), key, version, Events.TemplateRemoved);
        return Results.NoContent();
    }
}
