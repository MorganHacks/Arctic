using System.Net;
using System.Text.RegularExpressions;

namespace MorganHacks.Lark.Data.Domain;

public static partial class EmailPreheader
{
    [GeneratedRegex("<body\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Body { get; }

    public static string Add(string html, string? previewText)
    {
        if (string.IsNullOrWhiteSpace(previewText))
        {
            return html;
        }

        var preheader = "<div aria-hidden=\"true\" style=\"display:none;"
            + "font-size:1px;line-height:1px;max-height:0;max-width:0;"
            + "opacity:0;overflow:hidden;mso-hide:all;\">"
            + WebUtility.HtmlEncode(previewText.Trim()) + "</div>";
        var body = Body.Match(html);
        return html.Insert(body.Success ? body.Index + body.Length : 0, preheader);
    }
}
