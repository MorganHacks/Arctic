using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api;

public static partial class TemplateEndpoints
{
    public sealed record TemplateVisibilityRequest(string[]? Keys, bool? Hidden);

    private static async Task<IResult> SetVisibility(
        TemplateVisibilityRequest? body, HttpContext http, TemplateVisibilityStore visibility, CancellationToken ct)
    {
        if (body?.Keys is not { Length: > 0 and <= 200 } keys || body.Hidden is null
            || keys.Any(key => string.IsNullOrEmpty(key) || key.Length > MaxKeyLength || !Key.IsMatch(key)))
            return Results.BadRequest(new { error = "Choose between 1 and 200 valid templates and whether to hide them." });

        var person = http.PersonId();
        if (!await visibility.SetHiddenAsync(person, keys.Distinct().ToArray(), body.Hidden.Value, ct))
            return Results.NotFound(new { error = "One or more templates are no longer available. Refresh the gallery and try again." });

        return Results.Ok(new { hiddenKeys = await visibility.ListAsync(person, ct) });
    }
}
