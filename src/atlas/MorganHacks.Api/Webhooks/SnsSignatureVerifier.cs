using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace MorganHacks.Api.Webhooks;

public interface ISnsSignatureVerifier
{
    Task<bool> IsAuthenticAsync(SnsMessage message, CancellationToken ct = default);
}

/// <summary>
/// Decides whether an SNS message really came from AWS.
/// </summary>
/// <remarks>
/// This endpoint writes to the suppression list. Without verification, anyone
/// who finds the URL can post a bounce for any address and stop that person
/// receiving email from us — including their sign-in link. That is a denial of
/// service against individual applicants, and it would look exactly like a
/// deliverability problem while it happened.
/// </remarks>
public sealed partial class SnsSignatureVerifier(
    HttpClient client,
    IMemoryCache cache,
    ILogger<SnsSignatureVerifier> log) : ISnsSignatureVerifier
{
    /// <summary>
    /// Which hosts may serve a signing certificate.
    /// </summary>
    /// <remarks>
    /// The most important line here. <c>SigningCertURL</c> arrives inside the
    /// unverified message, so without this check an attacker points it at a
    /// certificate they control, signs their own payload with the matching
    /// key, and every signature verifies perfectly. Checking the signature but
    /// not where the key came from is the same as not checking it at all.
    /// </remarks>
    [GeneratedRegex(@"^sns\.[a-z0-9\-]+\.amazonaws\.com(\.cn)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TrustedCertHost { get; }

    public static bool IsTrustedCertUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && TrustedCertHost.IsMatch(uri.Host);

    public async Task<bool> IsAuthenticAsync(SnsMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(message.Signature) || !IsTrustedCertUrl(message.SigningCertUrl))
        {
            log.LogWarning(
                "Rejected an SNS message with a missing signature or untrusted certificate host.");
            return false;
        }

        var canonical = message.CanonicalBytes();
        if (canonical is null)
        {
            log.LogWarning("Rejected an SNS message of an unrecognised type.");
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(message.Signature);
        }
        catch (FormatException)
        {
            return false;
        }

        var certificate = await CertificateAsync(message.SigningCertUrl!, ct);
        if (certificate is null)
        {
            return false;
        }

        // Version 1 is SHA1, version 2 is SHA256. Anything else is not
        // something we know how to check, so it is refused rather than
        // guessed at.
        var algorithm = message.SignatureVersion switch
        {
            "1" => HashAlgorithmName.SHA1,
            "2" => HashAlgorithmName.SHA256,
            _ => (HashAlgorithmName?)null,
        };

        if (algorithm is null)
        {
            log.LogWarning(
                "Rejected an SNS message with signature version {Version}.",
                message.SignatureVersion);
            return false;
        }

        using var rsa = certificate.GetRSAPublicKey();
        return rsa is not null
               && rsa.VerifyData(canonical, signature, algorithm.Value, RSASignaturePadding.Pkcs1);
    }

    /// <remarks>
    /// Cached because AWS rotates these rarely and fetching one per webhook
    /// would make an outbound request the cost of every bounce notification —
    /// and during a bad blast those arrive in bulk.
    /// </remarks>
    private async Task<X509Certificate2?> CertificateAsync(string url, CancellationToken ct)
    {
        if (cache.TryGetValue(url, out X509Certificate2? cached))
        {
            return cached;
        }

        try
        {
            var pem = await client.GetStringAsync(url, ct);
            var certificate = X509Certificate2.CreateFromPem(pem);
            cache.Set(url, certificate, TimeSpan.FromHours(12));
            return certificate;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not read the SNS signing certificate.");
            return null;
        }
    }
}
