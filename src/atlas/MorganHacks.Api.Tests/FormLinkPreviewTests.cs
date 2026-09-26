using System.Net;
using System.Text;

namespace MorganHacks.Api.Tests;

public class FormLinkPreviewTests
{
    [Theory]
    [InlineData("http://localhost/video")]
    [InlineData("http://127.0.0.1/video")]
    [InlineData("http://2130706433/video")]
    [InlineData("http://[::1]/video")]
    [InlineData("http://169.254.169.254/video")]
    [InlineData("http://intranet.local/video")]
    [InlineData("https://user:secret@example.com/video")]
    [InlineData("https://example.com:444/video")]
    [InlineData("file:///video")]
    public async Task Private_or_invalid_links_never_make_a_request(string url)
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(_ => { calls++; return Html(""); }));
        Assert.Equal(FormLinkMedia.Empty, await new FormLinkPreview(client).ResolveAsync(url, default));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Redirects_cannot_reach_private_hosts()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://127.0.0.1/video") } };
        }));
        Assert.Equal(FormLinkMedia.Empty, await new FormLinkPreview(client).ResolveAsync("https://example.com/private-redirect", default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Relative_redirects_resolve_metadata_against_the_final_page()
    {
        using var client = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath == "/relative-start"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/watch/recap", UriKind.Relative) } }
            : Html("<meta content='../poster.jpg?x=1&amp;y=2' property='og:image'><meta property='og:video:type' content='video/mp4'><meta property='og:video' content='recap.mp4'>")));
        Assert.Equal(new FormLinkMedia("https://example.com/poster.jpg?x=1&y=2", "https://example.com/watch/recap.mp4"),
            await new FormLinkPreview(client).ResolveAsync("https://example.com/relative-start", default));
    }

    [Fact]
    public void Player_pages_are_not_treated_as_video_files()
    {
        var media = FormLinkPreview.ReadMetadata("<meta property=og:video content='https://video.example.com/embed/123'><meta name='twitter:image' content='https://images.example.com/123.jpg'>", new Uri("https://example.com/recap"));
        Assert.Equal(new FormLinkMedia("https://images.example.com/123.jpg", null), media);
    }

    [Fact]
    public void Unsafe_metadata_never_becomes_an_image_or_video()
    {
        Assert.Equal(FormLinkMedia.Empty, FormLinkPreview.ReadMetadata("<meta property='og:image' content='http://169.254.169.254/image'><meta property='og:video' content='javascript:alert(1)'>", new Uri("https://example.com/recap")));
    }

    [Fact]
    public async Task Vimeo_uses_its_thumbnail_metadata_without_embedding_a_player()
    {
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal("vimeo.com", request.RequestUri!.Host);
            Assert.Equal("/api/oembed.json", request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"thumbnail_url\":\"https://i.vimeocdn.com/video/123.jpg\",\"html\":\"<iframe></iframe>\"}", Encoding.UTF8, "application/json") };
        }));
        Assert.Equal(new FormLinkMedia("https://i.vimeocdn.com/video/123.jpg", null),
            await new FormLinkPreview(client).ResolveAsync("https://vimeo.com/12345678", default));
    }

    [Fact]
    public async Task Unavailable_pages_keep_the_card_usable_without_media()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        Assert.Equal(FormLinkMedia.Empty, await new FormLinkPreview(client).ResolveAsync("https://example.com/forbidden-preview", default));
    }

    [Fact]
    public async Task Metadata_reads_are_bounded()
    {
        using var client = new HttpClient(new Handler(_ => Html(new string(' ', 256_000) + "<meta property='og:image' content='https://example.com/too-late.jpg'>")));
        Assert.Equal(FormLinkMedia.Empty, await new FormLinkPreview(client).ResolveAsync("https://example.com/oversized-preview", default));
    }

    [Fact]
    public async Task Video_content_is_used_without_downloading_the_movie()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnreadableStream()) { Headers = { ContentType = new("video/mp4") } },
        }));
        Assert.Equal(new FormLinkMedia(null, "https://example.com/video-stream"),
            await new FormLinkPreview(client).ResolveAsync("https://example.com/video-stream", default));
    }

    [Fact]
    public async Task Repeated_reads_reuse_the_cached_preview()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(_ => { calls++; return Html("<meta property='og:image' content='https://example.com/cached.jpg'>"); }));
        var preview = new FormLinkPreview(client);
        var url = "https://example.com/cache-" + Guid.NewGuid();
        var first = await preview.ResolveAsync(url, default);
        Assert.Equal(first, await preview.ResolveAsync(url, default));
        Assert.Equal(1, calls);
    }

    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
    private sealed class UnreadableStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Video body must not be read.");
    }
}
