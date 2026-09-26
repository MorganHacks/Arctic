using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace MorganHacks.Api;

public sealed record FormLinkMedia(string? Image, string? Video)
{
    public static readonly FormLinkMedia Empty = new(null, null);
}

public sealed class FormLinkPreview(HttpClient client)
{
    private const int MaxBytes = 256_000;
    private static readonly MemoryCache Cache = new(new MemoryCacheOptions { SizeLimit = 512 });
    private static readonly SemaphoreSlim Downloads = new(8);
    private static readonly Regex MetaTags = new(@"<meta\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Attributes = new("([\\w:-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.None, TimeSpan.FromMilliseconds(100));

    public static Uri? PublicUrl(string? value, Uri? origin = null)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsWhiteSpace) || value.Contains('\\')) return null;
        if (!Uri.TryCreate(origin, value, out var uri) || !uri.IsAbsoluteUri
            || uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return null;
        var host = uri.IdnHost.TrimEnd('.');
        if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
            return TemplateHtmlImporter.IsPublicAddress(address) ? uri : null;
        return host.Contains('.') && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ? uri : null;
    }

    public async Task<FormLinkMedia> ResolveAsync(string? value, CancellationToken ct)
    {
        var uri = PublicUrl(value);
        if (uri is null) return FormLinkMedia.Empty;
        if (Cache.TryGetValue(uri.AbsoluteUri, out FormLinkMedia? cached)) return cached!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        var entered = false;
        try
        {
            await Downloads.WaitAsync(timeout.Token);
            entered = true;
            if (Cache.TryGetValue(uri.AbsoluteUri, out cached)) return cached!;
            var result = await DownloadAsync(uri, timeout.Token);
            Cache.Set(uri.AbsoluteUri, result, new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = result == FormLinkMedia.Empty ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(30),
            });
            return result;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or JsonException or RegexMatchTimeoutException)
        {
            return FormLinkMedia.Empty;
        }
        finally { if (entered) Downloads.Release(); }
    }

    private async Task<FormLinkMedia> DownloadAsync(Uri original, CancellationToken ct)
    {
        var vimeo = original.Host is "vimeo.com" or "www.vimeo.com" or "player.vimeo.com";
        var uri = vimeo ? new Uri("https://vimeo.com/api/oembed.json?url=" + Uri.EscapeDataString(original.AbsoluteUri)) : original;
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/json,video/*;q=0.5");
            request.Headers.UserAgent.ParseAdd("Arctic-LinkPreview/1.0");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (redirects == 3 || PublicUrl(response.Headers.Location?.ToString(), uri) is not { } next) return FormLinkMedia.Empty;
                uri = next;
                continue;
            }
            if (!response.IsSuccessStatusCode) return FormLinkMedia.Empty;
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is "video/mp4" or "video/webm" or "video/ogg" or "video/quicktime")
                return new FormLinkMedia(null, uri.AbsoluteUri);
            if (contentType is not ("text/html" or "application/xhtml+xml" or "application/json")) return FormLinkMedia.Empty;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            while (bytes.Length < MaxBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, MaxBytes - bytes.Length)), ct);
                if (read == 0) break;
                bytes.Write(buffer, 0, read);
            }
            var body = Encoding.UTF8.GetString(bytes.ToArray());
            if (vimeo && contentType == "application/json")
            {
                using var json = JsonDocument.Parse(body);
                var image = json.RootElement.TryGetProperty("thumbnail_url", out var thumbnail) && thumbnail.ValueKind == JsonValueKind.String
                    ? PublicUrl(thumbnail.GetString())?.AbsoluteUri : null;
                return new FormLinkMedia(image, null);
            }
            return ReadMetadata(body, uri);
        }
        return FormLinkMedia.Empty;
    }

    public static FormLinkMedia ReadMetadata(string html, Uri origin)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTags.Matches(html))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in Attributes.Matches(tag.Value))
            {
                var value = attribute.Groups[2].Success ? attribute.Groups[2].Value
                    : attribute.Groups[3].Success ? attribute.Groups[3].Value : attribute.Groups[4].Value;
                attributes[attribute.Groups[1].Value] = WebUtility.HtmlDecode(value);
            }
            var name = attributes.GetValueOrDefault("property") ?? attributes.GetValueOrDefault("name");
            if (name is not null && attributes.TryGetValue("content", out var content)) metadata.TryAdd(name, content);
        }
        string? Media(params string[] names) => names.Select(name => PublicUrl(metadata.GetValueOrDefault(name), origin)?.AbsoluteUri).FirstOrDefault(url => url is not null);
        var image = Media("og:image:secure_url", "og:image", "twitter:image", "twitter:image:src");
        var video = Media("og:video:secure_url", "og:video:url", "og:video");
        var type = metadata.GetValueOrDefault("og:video:type");
        if (video is not null && type is not ("video/mp4" or "video/webm" or "video/ogg" or "video/quicktime")
            && !Regex.IsMatch(new Uri(video).AbsolutePath, @"\.(mp4|webm|ogv|mov)$", RegexOptions.IgnoreCase)) video = null;
        return new FormLinkMedia(image, video);
    }
}
