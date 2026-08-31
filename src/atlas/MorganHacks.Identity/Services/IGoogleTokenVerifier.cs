using MorganHacks.Identity.Domain;

namespace MorganHacks.Identity.Services;

/// <summary>
/// Verifies a Google ID token and reports who it belongs to.
/// </summary>
/// <remarks>
/// An interface so the sign-in rules can be tested without standing up a fake
/// Google. The real implementation validates signature, issuer, audience and
/// expiry; a token that fails any of those yields null and nothing downstream
/// ever sees it.
/// </remarks>
public interface IGoogleTokenVerifier
{
    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct = default);
}
