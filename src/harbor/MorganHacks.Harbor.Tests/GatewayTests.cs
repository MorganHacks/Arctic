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
    public async Task Asking_who_is_signed_in_is_not_throttled_like_signing_in()
    {
        // The console calls /auth/me on every page it renders. Under the
        // strict auth limiter that made the eleventh page in a quarter of an
        // hour a sign-out — and the limiter partitions on IP, so on campus
        // that is one building sharing ten page views between everybody.
        //
        // Atlas deliberately does not throttle this endpoint for exactly that
        // reason. The gateway was quietly overruling it.
        for (var i = 0; i < 20; i++)
        {
            var response = await Client().GetAsync("/api/auth/me");

            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Asking_for_a_sign_in_link_is_still_throttled()
    {
        // The other half. Unlimited, this endpoint is a way to send mail from
        // our domain to any address somebody names, which costs the sending
        // reputation the domain spent weeks earning.
        var last = HttpStatusCode.OK;

        for (var i = 0; i < 20; i++)
        {
            last = (await Client().PostAsync("/api/auth/magic-link", content: null)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
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
