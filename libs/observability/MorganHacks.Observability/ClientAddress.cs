using Microsoft.AspNetCore.Http;

namespace MorganHacks.Observability;

/// <summary>
/// Who a request is actually from, for the purpose of rate limiting.
/// </summary>
/// <remarks>
/// Both front ends call the API from their own server rather than from the
/// browser, because the session cookie is SameSite=Lax and a browser will not
/// send one cross-site. That is the right call and it has a consequence: the
/// connection harbor sees comes from Vercel, so every applicant in the world
/// arrives from the same handful of addresses.
/// <para>
/// The first version of this read <c>X-Real-IP</c> unconditionally, and argued
/// in this comment that trusting it was acceptable. It was not. Harbor has a
/// public hostname, so anybody could send a different value on every request
/// and get a fresh bucket each time. Measured against staging: twelve requests
/// with no header gave five 202s and then 429; twelve with a varying
/// <c>X-Real-IP</c> gave twelve 202s. The limit was not weakened, it was gone.
/// </para>
/// <para>
/// So the header is now believed only when the request also carries the shared
/// secret, which only the front ends have. A request without it is bucketed on
/// the address the socket reports, which for proxied traffic is Vercel — the
/// shared bucket this was written to fix. That is the right way round: a
/// shared bucket is a worse limit, and an unauthenticated bypass is no limit.
/// </para>
/// <para>
/// It fails closed. No secret configured means no header is ever believed,
/// because the alternative is an environment that is quietly wide open because
/// somebody missed a variable.
/// </para>
/// </remarks>
public static class ClientAddress
{
    /// <summary>Set by Vercel on a proxied request. Not set by a browser.</summary>
    private const string RealIp = "X-Real-IP";

    private const string VercelFor = "X-Vercel-Forwarded-For";

    /// <summary>
    /// Proof the request came through one of our front ends rather than from
    /// somebody typing at harbor's public hostname.
    /// </summary>
    public const string ProxySecretHeader = "X-MH-Proxy";

    /// <summary>The best available identity for a rate-limit partition.</summary>
    /// <param name="expectedSecret">
    /// The configured shared secret. Empty or null means the forwarded headers
    /// are never believed.
    /// </param>
    public static string ForRateLimit(HttpContext http, string? expectedSecret)
    {
        if (Trusted(http, expectedSecret))
        {
            foreach (var header in new[] { VercelFor, RealIp })
            {
                var value = http.Request.Headers[header].ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    // First entry only. These carry a list when there are
                    // several hops, and the leftmost is the client -- taking
                    // the whole string would make every distinct chain its own
                    // bucket.
                    var first = value.Split(',')[0].Trim();
                    if (first.Length is > 0 and <= 45)
                    {
                        return first;
                    }
                }
            }
        }

        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static bool Trusted(HttpContext http, string? expectedSecret)
    {
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            return false;
        }

        var presented = http.Request.Headers[ProxySecretHeader].ToString();

        // Length-independent comparison. The secret is compared on every
        // rate-limited request, which is exactly the shape of thing worth not
        // leaking a byte at a time.
        return !string.IsNullOrEmpty(presented)
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(presented),
                System.Text.Encoding.UTF8.GetBytes(expectedSecret));
    }
}
