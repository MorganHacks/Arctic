using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using MorganHacks.Harbor;

namespace MorganHacks.Harbor.Tests;

/// <summary>
/// Proves that identity a caller supplied never reaches the service behind
/// harbor.
/// </summary>
/// <remarks>
/// The gateway doc calls this out as the one-line mistake worth writing a test
/// for: if someone sends <c>X-Person-Id</c> from outside and harbor forwards
/// it, we have handed out the ability to act as anyone.
/// <para>
/// Asserting on harbor's own request object would prove nothing about what was
/// forwarded, so this stands up a real upstream that reports back exactly
/// which headers arrived.
/// </para>
/// </remarks>
public sealed class ImpersonationTests : IAsyncLifetime
{
    private WebApplication _upstream = null!;
    private WebApplicationFactory<Program> _harbor = null!;
    private string _upstreamUrl = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _upstream = builder.Build();

        // Echoes the headers it actually received.
        _upstream.MapGet("/applications/echo", (HttpContext http) =>
            Results.Ok(new
            {
                personId = http.Request.Headers[IdentityHeaders.PersonId].ToString(),
                permissions = http.Request.Headers[IdentityHeaders.Permissions].ToString(),
                correlationId = http.Request.Headers[IdentityHeaders.CorrelationId].ToString(),
            }));

        await _upstream.StartAsync();
        _upstreamUrl = _upstream.Urls.First();

        _harbor = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting(
                "ReverseProxy:Clusters:atlas:Destinations:primary:Address", _upstreamUrl + "/"));
    }

    public async Task DisposeAsync()
    {
        _harbor.Dispose();
        await _upstream.StopAsync();
        await _upstream.DisposeAsync();
    }

    [Fact]
    public async Task A_caller_supplied_person_id_never_reaches_the_service()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications/echo");
        request.Headers.Add(IdentityHeaders.PersonId, "11111111-1111-1111-1111-111111111111");
        request.Headers.Add(IdentityHeaders.Permissions, "applications.export");

        var response = await _harbor.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("11111111-1111-1111-1111-111111111111", body);
        Assert.DoesNotContain("applications.export", body);
    }

    [Fact]
    public async Task The_correlation_id_does_reach_the_service()
    {
        // Stripping is targeted, not blanket: tracing depends on this one
        // surviving the hop.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/applications/echo");
        request.Headers.Add(IdentityHeaders.CorrelationId, "trace-me-123");

        var response = await _harbor.CreateClient().SendAsync(request);

        Assert.Contains("trace-me-123", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_request_that_supplies_no_identity_still_reaches_the_service()
    {
        // The strip must not break ordinary traffic.
        var response = await _harbor.CreateClient().GetAsync("/api/applications/echo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
