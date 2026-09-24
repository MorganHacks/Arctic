using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Api;

public static partial class TemplateEndpoints
{
    private static async Task<IResult> DiscardDraft(
        string key, HttpContext http, TemplateCatalog templates, TemplateDraftStore drafts, CancellationToken ct)
    {
        if (await templates.FindAsync(key, ct) is null)
            return Results.NotFound(new { error = NoSuchTemplate });
        await drafts.DeleteAsync(key, http.PersonId(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SaveSettings(
        TemplateRequest? request, HttpContext http, TemplateCatalog templates,
        TemplateDraftStore drafts, CancellationToken ct)
    {
        var key = request?.Key?.Trim();
        if (string.IsNullOrEmpty(key)) key = $"template_{Guid.NewGuid():N}";
        if (key.Length > MaxKeyLength || !Key.IsMatch(key))
            return Results.BadRequest(new { error = BadKey });

        if (!TryDraft(request, key, out var draft, out var refusal, settingsOnly: true))
            return Results.BadRequest(new { error = refusal });

        if (string.IsNullOrWhiteSpace(draft!.FromName))
            return Results.BadRequest(new { error = "Sender name is required." });

        var current = await templates.FindAsync(key, ct);
        if (current is not null && current.Kind != draft.Kind)
            return Results.Conflict(new { error = KindIsFixed(current.Kind) });

        var previous = await drafts.FindAsync(key, http.PersonId(), ct);
        if (previous is not null && previous.BaseVersion != current?.Version)
            return Results.Conflict(new { error = "This template has changed since your draft was started. Copy any changes you want to keep, then reload the latest version." });

        draft = draft with { Name = draft.Name ?? current?.Name ?? CampaignName(draft.Subject) };
        var saved = await drafts.SaveAsync(draft, http.PersonId(), current?.Version, ct);
        return Results.Ok(DraftDetail(saved));
    }

    private static TemplateSummary DraftSummary(WorkingTemplate saved, string? previewHtml = null) => new(
        saved.Content.Key,
        saved.Content.Name ?? saved.Content.Key,
        saved.Content.Kind,
        saved.Content.Subject,
        saved.Content.Format,
        saved.BaseVersion ?? 0,
        saved.UpdatedAt,
        true,
        previewHtml);

    private static object DraftDetail(WorkingTemplate saved) => new
    {
        key = saved.Content.Key,
        name = saved.Content.Name,
        settingsComplete = true,
        designComplete = false,
        hasDraft = true,
        kind = saved.Content.Kind,
        subject = saved.Content.Subject,
        format = saved.Content.Format,
        body = saved.Content.Source,
        previewText = saved.Content.PreviewText,
        clickTracking = saved.Content.ClickTracking,
        html = "",
        text = "",
        fromName = saved.Content.FromName,
        fromLocal = saved.Content.FromLocal,
        fromDomain = saved.Content.FromDomain,
        replyTo = saved.Content.ReplyTo,
        version = saved.BaseVersion ?? 0,
        placeholders = Array.Empty<string>(),
    };
}
