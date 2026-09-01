using System.Text.RegularExpressions;

namespace MorganHacks.Observability;

/// <summary>
/// What must never reach a log line or an error report.
/// </summary>
/// <remarks>
/// Sentry's own scrubbing knows about passwords and card numbers. It does not
/// know that <c>resume_key</c> points at somebody's CV or that
/// <c>responses</c> is the whole answer set from a form, so the list has to be
/// ours.
/// <para>
/// The rule everywhere else in this codebase is to log <c>person_id</c> rather
/// than an address. This is the net underneath that rule, for the places
/// somebody forgets — a query string on a captured request, a breadcrumb, an
/// exception message that happens to include what was being inserted.
/// </para>
/// </remarks>
public static partial class Redaction
{
    public const string Placeholder = "[redacted]";

    /// <summary>
    /// Keys whose values never leave the process.
    /// </summary>
    /// <remarks>
    /// Matched on the key rather than the value, because matching values means
    /// guessing what an email address looks like and being wrong about
    /// somebody's.
    /// </remarks>
    public static readonly IReadOnlySet<string> SensitiveKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // People
            "email", "to_email", "toEmail", "phone", "first_name", "last_name",
            "full_name", "fullName", "dietary_needs", "accessibility_needs",

            // The whole free-form answer set, whatever it ends up holding
            "responses",

            // Somebody's CV
            "resume_key", "resumeKey", "resume_filename",

            // Credentials and bearer values. A magic link in a log is a
            // working sign-in for anyone who can read logs.
            "token", "link", "password", "secret", "signature",
            "code_verifier", "client_secret", "authorization", "cookie",
        };

    /// <summary>Redacts a value if its key is one we never keep.</summary>
    public static string? Scrub(string key, string? value) =>
        SensitiveKeys.Contains(key) ? Placeholder : Mask(value);

    /// <summary>
    /// Masks anything that looks like an address, wherever it turned up.
    /// </summary>
    /// <remarks>
    /// The key-based list above is the real defence. This catches addresses
    /// embedded in free text — a database error quoting the row it rejected,
    /// most often — where there is no key to match on.
    /// </remarks>
    public static string? Mask(string? value) =>
        value is null ? null : EmailLike().Replace(value, Placeholder);

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")]
    private static partial Regex EmailLike();
}
