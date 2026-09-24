using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MorganHacks.Lark.Data.Domain;

public static partial class EmailLinks
{
    [GeneratedRegex("https?://[^\\s<>\"']+", RegexOptions.IgnoreCase)]
    private static partial Regex TextUrl { get; }

    public static bool IsWebUrl(string value) =>
        value.Length <= 4096
        && !value.Any(char.IsControl)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http"
        && uri.Host.Length > 0
        && uri.UserInfo.Length == 0;

    public static IReadOnlyList<string> Destinations(string html) =>
        Anchors(html).Select(anchor => anchor.Destination)
            .Distinct(StringComparer.Ordinal).ToArray();

    public static string RewriteHtml(string html, IReadOnlyDictionary<string, string> links)
    {
        var output = new StringBuilder(html.Length);
        var offset = 0;
        foreach (var anchor in Anchors(html))
        {
            if (!links.TryGetValue(anchor.Destination, out var tracked))
            {
                continue;
            }

            output.Append(html, offset, anchor.Start - offset);
            output.Append("<a");
            foreach (var (name, value) in anchor.Tag.Attributes)
            {
                output.Append(' ').Append(name).Append("=\"")
                    .Append(WebUtility.HtmlEncode(name == "href" ? tracked : WebUtility.HtmlDecode(value)))
                    .Append('"');
            }
            output.Append('>');
            offset = anchor.End;
        }
        output.Append(html, offset, html.Length - offset);
        return output.ToString();
    }

    public static string RewriteText(string text, IReadOnlyDictionary<string, string> links) =>
        TextUrl.Replace(text, match =>
        {
            if (links.TryGetValue(match.Value, out var exact))
            {
                return exact;
            }
            var url = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}');
            return links.TryGetValue(url, out var tracked)
                ? tracked + match.Value[url.Length..]
                : match.Value;
        });

    private sealed record Anchor(int Start, int End, string Destination, EmailHtml.Tag Tag);

    private static IEnumerable<Anchor> Anchors(string html)
    {
        var offset = 0;
        while ((offset = html.IndexOf('<', offset)) >= 0)
        {
            if (html.AsSpan(offset).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = html.IndexOf("-->", offset + 4, StringComparison.Ordinal);
                offset = end < 0 ? html.Length : end + 3;
                continue;
            }
            if (!EmailHtml.TryReadTag(html, offset, out var tag, out var next))
            {
                offset++;
                continue;
            }
            if (!tag.Closing && tag.Name is "style" or "script")
            {
                offset = EmailHtml.SkipPast(html, tag.Name, next);
                continue;
            }
            if (tag.Name == "a" && !tag.Closing)
            {
                var href = tag.Attributes.FirstOrDefault(attribute => attribute.Name == "href").Value;
                var destination = WebUtility.HtmlDecode(href ?? string.Empty).Trim();
                if (IsWebUrl(destination))
                {
                    yield return new Anchor(offset, next, destination, tag);
                }
            }
            offset = next;
        }
    }
}
