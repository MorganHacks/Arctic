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
    /// Every placeholder a template asks for, across all three parts.
    /// </summary>
    /// <remarks>
    /// So a caller can find out before it renders whether it holds the values
    /// a template needs. <see cref="Fill"/> leaves an unknown placeholder
    /// standing, which is the right behaviour for one message somebody is
    /// testing and the wrong outcome for four hundred that cannot be recalled:
    /// "Hi {{firstName}}" is only a useful mistake if somebody reads it before
    /// it goes out.
    /// <para>
    /// Here rather than in the caller because the regex that decides what a
    /// placeholder is lives here. Two copies of it would agree until one was
    /// changed.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> PlaceholdersIn(EmailTemplate template)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in new[] { template.Subject, template.BodyHtml, template.BodyText })
        {
            foreach (Match match in Placeholder.Matches(part))
            {
                found.Add(match.Groups[1].Value);
            }
        }

        return found;
    }

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
