namespace MorganHacks.Api;

public static partial class TemplateEndpoints
{
    public sealed record TemplateImportRequest(string? Url);

    private static async Task<IResult> ImportHtml(TemplateImportRequest? request, TemplateHtmlImporter importer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Url))
            return Results.BadRequest(new { error = "Enter the public URL of your HTML email." });
        try
        {
            var body = await importer.ImportAsync(request.Url.Trim(), ct);
            return Results.Ok(new { body });
        }
        catch (TemplateImportException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (HttpRequestException) { return Results.BadRequest(new { error = "This URL could not be reached. Check that it is public and try again." }); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.BadRequest(new { error = "This URL took too long to respond. Try again or use another URL." });
        }
    }
}
