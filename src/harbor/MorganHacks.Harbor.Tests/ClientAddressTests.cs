using Microsoft.AspNetCore.Http;
using MorganHacks.Observability;

namespace MorganHacks.Harbor.Tests;

/// <summary>
/// Which caller a rate limit counts against, and who is allowed to say.
/// </summary>
/// <remarks>
/// Both front ends call the API from their own server, so the connection is
/// always from Vercel and partitioning on it puts every applicant in one
/// bucket. The forwarded header carries the real caller.
/// <para>
/// The first version of these tests asserted the header was preferred and
/// stopped there, which is why the bypass shipped: harbor has a public
/// hostname, so anybody could send a different value each time and get a fresh
/// bucket. Measured on staging, twelve requests with a varying header got
/// twelve 202s where five was the limit. Every test below now says who is
/// allowed to be believed, not only what is read.
/// </para>
/// </remarks>
public class ClientAddressTests
{
    private const string Secret = "a-shared-secret";

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

    private static HttpContext Proxied(params (string Header, string Value)[] headers) =>
        Request([.. headers, (ClientAddress.ProxySecretHeader, Secret)]);

    [Fact]
    public void A_caller_relayed_by_our_front_end_is_counted_as_themselves()
    {
        // The whole point. Without this every applicant shares Vercel's
        // address and one bucket.
        Assert.Equal(
            "158.103.2.6",
            ClientAddress.ForRateLimit(
                Proxied(("X-Vercel-Forwarded-For", "158.103.2.6")), Secret));

        Assert.Equal(
            "158.103.2.6",
            ClientAddress.ForRateLimit(Proxied(("X-Real-IP", "158.103.2.6")), Secret));
    }

    [Fact]
    public void A_header_from_anybody_else_is_not_believed()
    {
        // The bug this class exists for. Harbor has a public hostname; a
        // request that arrives without the shared secret is somebody talking
        // to it directly, and the address it claims is worth nothing.
        Assert.Equal(
            "54.88.105.85",
            ClientAddress.ForRateLimit(Request(("X-Real-IP", "203.0.113.7")), Secret));

        Assert.Equal(
            "54.88.105.85",
            ClientAddress.ForRateLimit(
                Request(("X-Vercel-Forwarded-For", "203.0.113.7")), Secret));
    }

    [Fact]
    public void A_wrong_secret_is_not_believed_either()
    {
        Assert.Equal(
            "54.88.105.85",
            ClientAddress.ForRateLimit(
                Request(
                    ("X-Real-IP", "203.0.113.7"),
                    (ClientAddress.ProxySecretHeader, "not-the-secret")),
                Secret));
    }

    [Fact]
    public void With_no_secret_configured_nothing_is_believed()
    {
        // Fails closed. An environment missing the variable gets a worse rate
        // limit, never an absent one.
        foreach (var configured in new string?[] { null, "", "   " })
        {
            Assert.Equal(
                "54.88.105.85",
                ClientAddress.ForRateLimit(
                    Request(
                        ("X-Real-IP", "203.0.113.7"),
                        (ClientAddress.ProxySecretHeader, "anything")),
                    configured));
        }
    }

    [Fact]
    public void The_connection_is_used_when_there_is_nothing_in_front()
    {
        Assert.Equal("54.88.105.85", ClientAddress.ForRateLimit(Request(), Secret));
    }

    [Fact]
    public void Only_the_first_entry_of_a_chain_is_taken()
    {
        // These carry a list when there are several hops and the leftmost is
        // the client. Taking the whole string would make every distinct chain
        // its own bucket, which is the same bypass by a different route.
        Assert.Equal(
            "158.103.2.6",
            ClientAddress.ForRateLimit(
                Proxied(("X-Real-IP", "158.103.2.6, 10.0.0.1, 10.0.0.2")), Secret));
    }

    [Fact]
    public void A_value_too_long_to_be_an_address_is_ignored()
    {
        Assert.Equal(
            "54.88.105.85",
            ClientAddress.ForRateLimit(
                Proxied(("X-Real-IP", new string('9', 400))), Secret));
    }

    [Fact]
    public void A_blank_header_falls_through_to_the_connection()
    {
        Assert.Equal(
            "54.88.105.85",
            ClientAddress.ForRateLimit(Proxied(("X-Real-IP", "   ")), Secret));
    }
}
