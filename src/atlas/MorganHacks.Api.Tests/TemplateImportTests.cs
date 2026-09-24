using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api.Tests;

public class TemplateImportTests
{
    [Theory]
    [InlineData("http://localhost/email.html")]
    [InlineData("http://127.0.0.1/email.html")]
    [InlineData("http://2130706433/email.html")]
    [InlineData("http://[::1]/email.html")]
    [InlineData("http://[::ffff:127.0.0.1]/email.html")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://192.168.1.1/email.html")]
    [InlineData("http://intranet.local/email.html")]
    [InlineData("https://example.com:444/email.html")]
    [InlineData("https://user:password@example.com/email.html")]
    [InlineData("file:///etc/passwd")]
    public async Task Private_or_non_web_urls_are_refused_before_a_request(string url)
    {
        var calls = 0;
        using var client = new HttpClient(new ResponseHandler(_ => { calls++; return Html("<p>Email</p>"); }));
        await Assert.ThrowsAsync<TemplateImportException>(() => new TemplateHtmlImporter(client).ImportAsync(url, default));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::ffff:192.168.1.1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2002:7f00:1::1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void Connections_only_allow_public_addresses(string address, bool allowed) =>
        Assert.Equal(allowed, TemplateHtmlImporter.IsPublicAddress(IPAddress.Parse(address)));

    [Fact]
    public async Task Redirects_cannot_reach_private_addresses()
    {
        var calls = 0;
        using var client = new HttpClient(new ResponseHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://127.0.0.1/") } };
        }));
        await Assert.ThrowsAsync<TemplateImportException>(() => new TemplateHtmlImporter(client).ImportAsync("https://example.com/email", default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Html_and_placeholders_survive_a_public_relative_redirect()
    {
        const string source = "<!doctype html><html><body><p>Hello {{firstName}} 👋</p></body></html>";
        using var client = new HttpClient(new ResponseHandler(request => request.RequestUri!.AbsolutePath == "/start"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/email.html", UriKind.Relative) } }
            : Html(source)));
        Assert.Equal(source, await new TemplateHtmlImporter(client).ImportAsync("https://example.com/start", default));
    }

    [Theory]
    [InlineData("application/json", "{\"email\":true}")]
    [InlineData("text/html", "<script>alert(1)</script>")]
    [InlineData("text/plain", "not an email")]
    public async Task Non_email_content_is_refused(string mediaType, string body)
    {
        using var client = new HttpClient(new ResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, mediaType) }));
        await Assert.ThrowsAsync<TemplateImportException>(() => new TemplateHtmlImporter(client).ImportAsync("https://example.com/email", default));
    }

    [Theory]
    [InlineData(200_001)]
    [InlineData(800_001)]
    public async Task Imported_html_is_bounded_without_a_content_length(int length)
    {
        using var client = new HttpClient(new ResponseHandler(_ =>
        {
            var content = new StreamContent(new UnseekableStream(Encoding.UTF8.GetBytes("<p>" + new string('a', length) + "</p>")));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        await Assert.ThrowsAsync<TemplateImportException>(() => new TemplateHtmlImporter(client).ImportAsync("https://example.com/email", default));
    }

    [Fact]
    public async Task Redirect_loops_are_bounded()
    {
        var calls = 0;
        using var client = new HttpClient(new ResponseHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://example.com/email") } };
        }));
        await Assert.ThrowsAsync<TemplateImportException>(() => new TemplateHtmlImporter(client).ImportAsync("https://example.com/email", default));
        Assert.Equal(4, calls);
    }

    internal static HttpResponseMessage Html(string source) => new(HttpStatusCode.OK)
    { Content = new StringContent(source, Encoding.UTF8, "text/html") };

    private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    internal sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}

public class TemplateImportEndpointTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>
{
    [Fact]
    public async Task Only_template_managers_can_import_html()
    {
        const string source = "<p>Imported {{firstName}}</p>";
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            builder.ConfigureServices(services => services.AddHttpClient<TemplateHtmlImporter>()
                .ConfigurePrimaryHttpMessageHandler(() => new TemplateImportTests.ResponseHandler(_ => TemplateImportTests.Html(source))));
        });
        using var client = app.CreateClient();
        var request = new { url = "https://example.com/email.html" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/admin/templates/import", request)).StatusCode);
        var id = await db.AddPersonAsync($"import-{Guid.NewGuid():N}@example.invalid");
        using var scope = app.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(id);
        client.DefaultRequestHeaders.Add("Cookie", $"mh_session={session}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/admin/templates/import", request)).StatusCode);
        await db.GrantAsync(id, "email.manage_templates");
        var response = await client.PostAsJsonAsync("/admin/templates/import", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(source, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("body").GetString());
        var invalid = await client.PostAsJsonAsync("/admin/templates/import", new { url = "http://localhost/email" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
