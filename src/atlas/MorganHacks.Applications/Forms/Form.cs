using System.Security.Cryptography;

namespace MorganHacks.Applications.Forms;

/// <summary>A form, and the link it lives behind.</summary>
/// <param name="RequiresSignIn">
/// Whether the link alone is enough. False is the original behaviour and the
/// default: the code is the whole permission. True means the form is for
/// people we already have on file, and the answers are tied to the person
/// rather than to an address somebody typed.
/// </param>
/// <param name="EligibleStatuses">
/// Which applicants may open it, as stored statuses. Per form rather than
/// global: an RSVP is for <c>accepted</c> and a feedback survey for
/// <c>checked_in</c>, and no single rule is right for both. Empty on a form
/// that does not require sign-in.
/// </param>
public sealed record Form(
    Guid Id,
    Guid EventId,
    string Code,
    string Name,
    string Kind,
    DateTimeOffset? ClosesAt,
    bool RequiresSignIn,
    IReadOnlyList<string> EligibleStatuses)
{
    /// <summary>Only this kind creates an applicant.</summary>
    public bool IsApplication => Kind == "application";

    /// <summary>
    /// Whether an endpoint must demand a session before showing this form.
    /// </summary>
    /// <remarks>
    /// The stored flag <em>and</em> the kind, every time it is asked. A check
    /// constraint already refuses to store the combination, so this can only
    /// disagree with the column if that constraint were ever dropped — and the
    /// failure it guards against is not one worth finding out about during
    /// registration week. Gating the application form makes applying
    /// impossible, because the account it would demand is created by applying.
    /// </remarks>
    public bool IsGated => RequiresSignIn && !IsApplication;

    public bool IsOpen(DateTimeOffset now) => ClosesAt is null || ClosesAt > now;

    /// <summary>Whether somebody in this status may open the form.</summary>
    /// <remarks>
    /// Nobody is eligible for a form with no audience, which is the safe way
    /// round: the schema refuses to store a gated form with an empty list, so
    /// reaching this with one means something is wrong, and the answer to
    /// "who may answer" while something is wrong is nobody.
    /// </remarks>
    public bool Admits(string? status) =>
        status is not null
        && EligibleStatuses.Contains(status, StringComparer.Ordinal);
}

/// <summary>Makes the code that goes in the URL.</summary>
/// <remarks>
/// Seven characters from an alphabet with no 0, O, 1 or l. These get read aloud
/// at a club meeting and written on a whiteboard, and the pairs people
/// mistranscribe are the ones worth removing rather than explaining.
/// <para>
/// Random rather than sequential. A guessable code turns an unlisted form into
/// a public one, and the whole point of a link is that having it is the
/// permission.
/// </para>
/// </remarks>
public static class FormCode
{
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    public const int Length = 7;

    /// <summary>
    /// Whether a string is shaped like a code we could have issued.
    /// </summary>
    /// <remarks>
    /// Not "is this a real form" — that is a database question and this is not
    /// a lookup. It exists for the one place a code arrives somewhere other
    /// than a route parameter and is used to build a URL: the <c>form</c> on a
    /// magic link. Anything with a slash, a scheme or a hostname in it is not
    /// a code, and refusing to recognise it is what stops that parameter being
    /// an open redirect.
    /// </remarks>
    public static bool Looks(string? code) =>
        code is { Length: Length } && code.All(Alphabet.Contains);

    public static string Next()
    {
        Span<char> code = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            code[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(code);
    }
}
