using System.Security.Cryptography;

namespace MorganHacks.Applications.Forms;

/// <summary>A form, and the link it lives behind.</summary>
public sealed record Form(
    Guid Id,
    Guid EventId,
    string Code,
    string Name,
    string Kind,
    DateTimeOffset? ClosesAt)
{
    /// <summary>Only this kind creates an applicant.</summary>
    public bool IsApplication => Kind == "application";

    public bool IsOpen(DateTimeOffset now) => ClosesAt is null || ClosesAt > now;
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
