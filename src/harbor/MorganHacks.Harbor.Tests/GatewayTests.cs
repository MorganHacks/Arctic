using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using MorganHacks.Harbor;

namespace MorganHacks.Harbor.Tests;

/// <summary>
/// What harbor guarantees before a request ever reaches atlas.
/// </summary>
public class GatewayTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _app;

    public GatewayTests(WebApplicationFactory<Program> app) => _app = app;

    private HttpClient Client() => _app.CreateClient();

    [Fact]
    public async Task Health_is_static_and_touches_nothing()
    {
        // Liveness must not depend on the database or on atlas. Harbor is the
        // only path to the API, so a dependency here turns a recoverable blip
        // into a full outage by restarting every pod at once.
        var response = await Client().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id()
    {
        var response = await Client().GetAsync("/api/health");

        var id = Assert.Single(response.Headers.GetValues(IdentityHeaders.CorrelationId));
        Assert.NotEmpty(id);
    }

    [Fact]
    public async Task An_inbound_correlation_id_is_kept()
    {
        // Keeping it is how one request stays identifiable across services.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add(IdentityHeaders.CorrelationId, "abc123");

        var response = await Client().SendAsync(request);

        Assert.Equal("abc123",
            Assert.Single(response.Headers.GetValues(IdentityHeaders.CorrelationId)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has spaces")]
    [InlineData("semi;colon")]
    [InlineData("way-too-long-way-too-long-way-too-long-way-too-long-way-too-long-way-too-long")]
    public async Task An_implausible_inbound_correlation_id_is_replaced(string supplied)
    {
        // It lands in every log line we write, so an unbounded caller-supplied
        // string is a log-injection surface.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.TryAddWithoutValidation(IdentityHeaders.CorrelationId, supplied);

        var response = await Client().SendAsync(request);

        var id = Assert.Single(response.Headers.GetValues(IdentityHeaders.CorrelationId));
        Assert.NotEqual(supplied, id);
        Assert.Equal(32, id.Length);
    }

    [Fact]
    public void Identity_headers_a_caller_may_not_supply_are_listed()
    {
        // The list is the security boundary. A header added to the forwarded
        // request without being added here is an impersonation hole.
        Assert.Contains(IdentityHeaders.PersonId, IdentityHeaders.CallerMustNotSupply);
        Assert.Contains(IdentityHeaders.Permissions, IdentityHeaders.CallerMustNotSupply);

        // The correlation id must NOT be stripped: accepting an inbound one is
        // deliberate, and stripping it would break tracing across services.
        Assert.DoesNotContain(IdentityHeaders.CorrelationId, IdentityHeaders.CallerMustNotSupply);
    }
}
