using Microsoft.AspNetCore.Http;

namespace MorganHacks.Observability;

/// <summary>
/// Who a request is actually from, for the purpose of rate limiting.
/// </summary>
/// <remarks>
/// Both front ends call the API from their own server rather than from the
/// browser, because the session cookie is SameSite=Lax and a browser will not
/// send one cross-site. That is the right call and it has a consequence:
/// the connection harbor sees comes from Vercel, so every applicant in the
/// world arrives from the same handful of addresses.
/// <para>
/// Measured against staging rather than assumed. A request straight to harbor
/// reports the caller; the same request through the console reports Vercel's
/// address, with the caller present in <c>X-Real-IP</c> and
/// <c>X-Vercel-Forwarded-For</c> — and <c>X-Forwarded-For</c> empty, which is
/// why the forwarded-headers middleware never found it.
/// </para>
/// <para>
/// This trusts a header, and that is worth being clear about. A caller
/// reaching harbor directly can put anything in it and get a fresh bucket. It
/// is still strictly better than today: right now there is one bucket for
/// everybody, so an ordinary applicant is limited by everyone else's traffic
/// and an attacker is barely limited at all. The controls that actually stop
/// abuse are elsewhere and unaffected — atlas limits per address, which is
/// what stops one person being mailed repeatedly, and volume from a single
/// source is Cloudflare's job, which is the only layer that sees the real
/// connection.
/// </para>
/// </remarks>
public static class ClientAddress
{
    /// <summary>Set by Vercel on a proxied request. Not set by a browser.</summary>
    private const string RealIp = "X-Real-IP";

    private const string VercelFor = "X-Vercel-Forwarded-For";

    /// <summary>The best available identity for a rate-limit partition.</summary>
    public static string ForRateLimit(HttpContext http)
    {
        foreach (var header in new[] { VercelFor, RealIp })
        {
            var value = http.Request.Headers[header].ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                // First entry only. These carry a list when there are several
                // hops, and the leftmost is the client — taking the whole
                // string would make every distinct chain its own bucket.
                var first = value.Split(',')[0].Trim();
                if (first.Length is > 0 and <= 45)
                {
                    return first;
                }
            }
        }

        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
