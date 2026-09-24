using System.Net;
using System.Net.Sockets;
using System.Text;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Api;

public sealed class TemplateImportException(string message) : Exception(message);

public sealed class TemplateHtmlImporter(HttpClient client)
{
    private const int MaxBytes = 800_000;
    private const int MaxCharacters = 200_000;
    private const string PublicUrlError = "Use a public HTTP or HTTPS URL for your email.";
    private static readonly IPNetwork[] BlockedNetworks = new[]
    {
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16",
        "172.16.0.0/12", "192.0.0.0/24", "192.0.2.0/24", "192.88.99.0/24", "192.168.0.0/16",
        "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4",
        "2001::/23", "2001:db8::/32", "2002::/16", "3fff::/20",
    }.Select(IPNetwork.Parse).ToArray();
    private static readonly IPNetwork GlobalV6 = IPNetwork.Parse("2000::/3");

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        MaxResponseHeadersLength = 32,
        ConnectCallback = ConnectPublicHost,
    };

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return !IPAddress.IsLoopback(address)
            && (address.AddressFamily == AddressFamily.InterNetwork || GlobalV6.Contains(address))
            && !BlockedNetworks.Any(network => network.Contains(address));
    }

    private static async ValueTask<Stream> ConnectPublicHost(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new HttpRequestException(PublicUrlError);
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("The email URL could not be reached.");
    }

    private static Uri PublicUrl(string value)
    {
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || !uri.IsDefaultPort || uri.UserInfo.Length > 0)
            throw new TemplateImportException(PublicUrlError);
        var host = uri.IdnHost.TrimEnd('.');
        if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            if (!IsPublicAddress(address)) throw new TemplateImportException(PublicUrlError);
        }
        else if (!host.Contains('.') || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                 || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                 || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new TemplateImportException(PublicUrlError);
        return uri;
    }

    public async Task<string> ImportAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        ct = timeout.Token;
        var uri = PublicUrl(url);
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("text/html, application/xhtml+xml, text/plain;q=0.8");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
                or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (redirects == 3 || response.Headers.Location is not { } location)
                    throw new TemplateImportException("This URL redirects too many times. Use the direct email URL.");
                uri = PublicUrl(new Uri(uri, location).AbsoluteUri);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new TemplateImportException("This URL could not be opened. Check that it is public and try again.");
            if (response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml" or "text/plain"))
                throw new TemplateImportException("This URL does not return HTML. Use the URL of an HTML email.");
            if (response.Content.Headers.ContentLength > MaxBytes)
                throw new TemplateImportException("This email is too large. Use HTML under 200,000 characters.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (bytes.Length + read > MaxBytes)
                    throw new TemplateImportException("This email is too large. Use HTML under 200,000 characters.");
                bytes.Write(buffer, 0, read);
            }
            var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
            Encoding encoding;
            try { encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset); }
            catch (ArgumentException) { throw new TemplateImportException("Use an HTML email saved with UTF-8 encoding."); }
            bytes.Position = 0;
            using var reader = new StreamReader(bytes, encoding, detectEncodingFromByteOrderMarks: true);
            var source = await reader.ReadToEndAsync(ct);
            if (source.Length > MaxCharacters)
                throw new TemplateImportException("This email is too large. Use HTML under 200,000 characters.");
            var (html, text) = TemplateBody.Render(TemplateBody.Html, source);
            if (!source.Contains('<') || string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(text))
                throw new TemplateImportException("This URL does not contain an HTML email. Try another URL.");
            return source;
        }
        throw new TemplateImportException("Use the direct email URL.");
    }
}
