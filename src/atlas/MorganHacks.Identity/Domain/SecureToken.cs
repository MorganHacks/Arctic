using System.Security.Cryptography;

namespace MorganHacks.Identity.Domain;

/// <summary>
/// A random secret handed to exactly one person, stored only as a hash.
/// </summary>
/// <remarks>
/// Used for both session tokens and magic-link tokens. The same reasoning as
/// passwords applies to both: a database leak should not hand out live
/// sessions or live login links, so the raw value exists only in the response
/// that creates it and in the holder's cookie or inbox.
/// </remarks>
public static class SecureToken
{
    /// <summary>
    /// 256 bits. Far past the point where guessing is the attack anyone would
    /// choose, and it keeps the encoded token a manageable length in a URL.
    /// </summary>
    private const int SizeInBytes = 32;

    /// <summary>
    /// Mints a new token. The raw value is returned once and never persisted;
    /// only <c>Hash</c> goes to the database.
    /// </summary>
    public static (string Raw, byte[] Hash) Issue()
    {
        var bytes = RandomNumberGenerator.GetBytes(SizeInBytes);

        // URL-safe: these travel in magic links and in cookies, and neither
        // wants '+', '/' or '=' surviving a round trip through an email
        // client that decides to be helpful.
        var raw = Base64UrlEncode(bytes);

        return (raw, Hash(raw));
    }

    /// <summary>
    /// Hashes a token presented by a caller so it can be looked up.
    /// </summary>
    /// <remarks>
    /// Plain SHA-256 rather than a password hash on purpose. A slow KDF exists
    /// to defend low-entropy human-chosen secrets against offline guessing;
    /// these are 256 random bits, so there is nothing to guess, and making
    /// every request pay a KDF would only add latency to the session lookup
    /// that happens on every single request.
    /// <para>
    /// Lookup is by exact hash match on a unique index, so there is no
    /// byte-by-byte comparison in our code to leak timing.
    /// </para>
    /// </remarks>
    public static byte[] Hash(string raw) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
