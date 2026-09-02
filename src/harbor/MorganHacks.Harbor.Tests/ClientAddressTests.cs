using Microsoft.AspNetCore.Http;
using MorganHacks.Observability;

namespace MorganHacks.Harbor.Tests;

/// <summary>
/// Which caller a rate limit counts against.
/// </summary>
/// <remarks>
/// Both front ends call the API from their own server, so the connection is
/// always from Vercel and partitioning on it puts every applicant in one
/// bucket. These assert the header that carries the real caller is preferred,
/// and that the connection is still used when nothing is in front.
/// </remarks>
public class ClientAddressTests
{
    private static HttpContext Request(params (string Header, string Value)[] headers)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("54.88.105.85");
        foreach (var (header, value) in headers)
        {
            http.Request.Headers[header] = value;
        }

        return http;
    }

    [Fact]
    public void The_caller_is_preferred_over_the_front_end_that_relayed_them()
    {
        // The whole point. Without this every applicant shares Vercel's
        // address, which is ten sign-ins a quarter of an hour for everybody.
        Assert.Equal("158.103.2.6",
            ClientAddress.ForRateLimit(Request(("X-Vercel-Forwarded-For", "158.103.2.6"))));
    }

    [Fact]
    public void X_Real_IP_works_too()
    {
        Assert.Equal("158.103.2.6",
            ClientAddress.ForRateLimit(Request(("X-Real-IP", "158.103.2.6"))));
    }

    [Fact]
    public void With_nothing_in_front_the_connection_is_the_caller()
    {
        // A request straight to harbor, which is what a health probe and an
        // attacker skipping the front end both look like.
        Assert.Equal("54.88.105.85", ClientAddress.ForRateLimit(Request()));
    }

    [Fact]
    public void Only_the_first_hop_counts()
    {
        // These carry a list when there are several proxies. Using the whole
        // string would make every distinct chain its own bucket, which is a
        // rate limit that never triggers.
        Assert.Equal("158.103.2.6",
            ClientAddress.ForRateLimit(Request(("X-Real-IP", "158.103.2.6, 10.0.0.1, 10.0.0.2"))));
    }

    [Fact]
    public void An_absurdly_long_value_is_ignored()
    {
        // It becomes a dictionary key held for the length of the window, so an
        // unbounded caller-supplied string is memory somebody else chooses.
        Assert.Equal("54.88.105.85",
            ClientAddress.ForRateLimit(Request(("X-Real-IP", new string('9', 400)))));
    }

    [Fact]
    public void An_empty_header_falls_through_rather_than_bucketing_everyone_together()
    {
        // Vercel sends X-Forwarded-For empty on a proxied request. Treating a
        // blank value as an identity would give every such request one bucket.
        Assert.Equal("54.88.105.85",
            ClientAddress.ForRateLimit(Request(("X-Real-IP", "   "))));
    }
}
