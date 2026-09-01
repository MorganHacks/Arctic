using System.Net;
using System.Text.RegularExpressions;

namespace MorganHacks.Lark.Data.Domain;

/// <summary>A template and the addresses it sends from.</summary>
public sealed record EmailTemplate(
    Guid Id,
    string Key,
    string Kind,
    string Subject,
    string BodyHtml,
    string BodyText,
    string FromLocal,
    string FromDomain,
    string? ReplyTo)
{
    /// <summary>Transactional sends jump the queue; broadcasts wait behind them.</summary>
    public bool IsTransactional => Kind == "transactional";

    public short Priority => IsTransactional ? (short)0 : (short)10;

    public string From => $"{FromLocal}@{FromDomain}";
}

/// <summary>One email, already rendered.</summary>
public sealed record RenderedEmail(string Subject, string BodyHtml, string BodyText);

/// <summary>
/// Fills <c>{{placeholders}}</c> in a template.
/// </summary>
/// <remarks>
/// Rendering happens once, at queue time, and the result is stored. If
/// somebody's name changes between queueing and sending, the email should say
/// what it said when it was approved — and a retry must never render
/// differently from the attempt it is retrying.
/// </remarks>
public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex Placeholder { get; }

    public static RenderedEmail Render(
        EmailTemplate template, IReadOnlyDictionary<string, string> values) =>
        new(Fill(template.Subject, values, escape: false),
            Fill(template.BodyHtml, values, escape: true),
            Fill(template.BodyText, values, escape: false));

    /// <summary>
    /// Substitutes values, escaping them in the HTML part.
    /// </summary>
    /// <remarks>
    /// The values are things people typed — names, school names, whatever a
    /// form collected. Dropping them into HTML unescaped is the same bug as
    /// rendering them into a page unescaped, and an email client is a browser.
    /// The text part is not escaped, because there is nothing to escape into.
    /// <para>
    /// A placeholder with no value is left as it is rather than emptied, so a
    /// missing variable reads as an obvious mistake in a test instead of a
    /// sentence with a hole in it that nobody notices.
    /// </para>
    /// </remarks>
    private static string Fill(
        string template, IReadOnlyDictionary<string, string> values, bool escape) =>
        Placeholder.Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value)
                ? escape ? WebUtility.HtmlEncode(value) : value
                : match.Value);
}
