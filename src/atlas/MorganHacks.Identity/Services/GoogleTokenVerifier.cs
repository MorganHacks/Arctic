using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using MorganHacks.Identity.Domain;

namespace MorganHacks.Identity.Services;

/// <summary>
/// Verifies a Google ID token against Google's published keys.
/// </summary>
/// <remarks>
/// Never trust an unverified token. Anyone can post a well-formed JWT claiming
/// to be anybody; the only thing that makes it evidence is the signature.
/// </remarks>
public sealed class GoogleTokenVerifier : IGoogleTokenVerifier
{
    // Google signs with keys that rotate. The manager caches the key set and
    // refreshes it, so rotation does not cause an outage.
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration =
        new("https://accounts.google.com/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

    private readonly string _clientId;

    public GoogleTokenVerifier(string clientId) => _clientId = clientId;

    public async Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct = default)
    {
        try
        {
            var configuration = await _configuration.GetConfigurationAsync(ct);

            var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = configuration.SigningKeys,

                    ValidateIssuer = true,
                    // Google issues both spellings and treats them as equivalent.
                    ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],

                    // Without this, a token minted for somebody else's app
                    // would validate here.
                    ValidateAudience = true,
                    ValidAudience = _clientId,

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                });

            if (!result.IsValid)
            {
                return null;
            }

            var subject = result.Claims.TryGetValue("sub", out var s) ? s?.ToString() : null;
            var email = result.Claims.TryGetValue("email", out var e) ? e?.ToString() : null;
            var verified = result.Claims.TryGetValue("email_verified", out var v)
                           && v?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            // An unverified address proves nothing: anyone can put any address
            // on a Google account they control until Google confirms it.
            if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(email) || !verified)
            {
                return null;
            }

            return new GoogleIdentity(subject, email);
        }
        catch
        {
            // Any failure is a failure to authenticate. Never fall through to
            // a partially validated token.
            return null;
        }
    }
}
