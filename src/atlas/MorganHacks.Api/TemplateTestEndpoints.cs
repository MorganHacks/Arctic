using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api;

public static partial class TemplateEndpoints
{
    public sealed record TemplateTestRequest(TemplateRequest? Draft, string? Recipient, Guid RequestId);

    private static async Task<IResult> SendTest(TemplateTestRequest? request, HttpContext http,
        TemplateTestQueue tests, MessageQueue queue, ILogger<TemplateTestRequest> log, CancellationToken ct)
    {
        var recipient = request?.Recipient?.Trim();
        if (string.IsNullOrEmpty(recipient) || !IsAddress(recipient))
            return Results.BadRequest(new { error = "Enter a valid recipient email address." });
        if (request!.RequestId == Guid.Empty)
            return Results.BadRequest(new { error = "Reopen the test email dialog and try again." });
        if (!TryDraft(request.Draft, "test", out var draft, out var refusal))
            return Results.BadRequest(new { error = refusal });
        if (await queue.IsSuppressedAsync(recipient, draft!.Kind == "transactional", ct))
            return Results.BadRequest(new { error = "This address cannot receive email. Use another test address." });
        var id = await tests.EnqueueAsync(draft, recipient, http.PersonId(), request.RequestId, ct);
        if (id is null) return Results.Conflict(new { error = "Reopen the test email dialog and try again." });
        log.LogInformation("A test email was queued. {actor} {message}", http.PersonId(), id);
        return Results.Accepted(value: new { id, status = "queued" });
    }
}
