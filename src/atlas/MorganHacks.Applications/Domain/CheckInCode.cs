using System.Security.Cryptography;

namespace MorganHacks.Applications.Domain;

/// <summary>
/// The code a confirmed hacker shows at the door.
/// </summary>
/// <remarks>
/// Twelve characters of Crockford base32, which is sixty bits of randomness in
/// something a person can read off a screen and a volunteer can type.
/// <para>
/// The shape follows from where it gets used. It is shown from a phone in a
/// queue, often from a screenshot taken hours earlier, on a network that is
/// carrying several hundred other phones — so it cannot rotate, cannot need a
/// round trip at the moment it is shown, and cannot depend on the phone's
/// clock being right. It is minted once, returned unchanged forever after, and
/// works from an image.
/// </para>
/// <para>
/// The cost of that is the obvious one: a code that never changes can be
/// forwarded. What answers it is not entropy or an expiry, it is the desk. A
/// scan names the person it belongs to, and the volunteer reading that name is
/// looking at whoever handed them the phone. Forwarding a code does not admit
/// the friend it was forwarded to; it marks the owner as arrived, which is a
/// trade nobody makes on purpose. An expiry would not have bought that, and
/// would have bought a queue of people whose screenshot went stale.
/// </para>
/// <para>
/// Crockford rather than plain base32 because of how these get read out. The
/// alphabet has no I, L, O or U in it, so nothing is confused with a one, a
/// zero, or a word nobody wants printed on a badge, and
/// <see cref="TryNormalise"/> folds the confusable characters back in for the
/// times somebody types it anyway.
/// </para>
/// </remarks>
public static class CheckInCode
{
    /// <summary>Crockford's base32 alphabet: no I, no L, no O, no U.</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// Twelve characters, which is sixty bits.
    /// </summary>
    /// <remarks>
    /// Sized against what it defends and what it costs. The endpoint that
    /// takes one already requires <c>checkin.scan</c>, so guessing is an
    /// attack available only to somebody who could check people in through the
    /// console anyway; sixty bits closes it regardless. Longer would only make
    /// the fallback harder to read out.
    /// </remarks>
    public const int Length = 12;

    /// <summary>How many characters sit in each group when it is shown.</summary>
    private const int GroupSize = 4;

    /// <summary>Mints a code. Random, never derived from anything about a person.</summary>
    /// <remarks>
    /// Rejection sampling rather than a modulo of a random byte. Thirty-two
    /// divides two hundred and fifty-six exactly, so the modulo would in fact
    /// be uniform here, but it stops being uniform the moment somebody changes
    /// the alphabet and nothing would say so.
    /// </remarks>
    public static string Issue()
    {
        var characters = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            characters[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(characters);
    }

    /// <summary>
    /// Reads a code somebody presented, in whatever shape it arrived.
    /// </summary>
    /// <remarks>
    /// Spaces and dashes are dropped because that is how it is displayed, and
    /// a volunteer typing what they can see should not have to know that the
    /// groups are decoration. Case is folded because a phone keyboard will
    /// have its own opinion. I and L become 1 and O becomes 0, which is
    /// Crockford's rule and the reason those letters are not in the alphabet:
    /// the character that was never issued is the one somebody typed by
    /// mistake, so mapping it is always the right guess.
    /// </remarks>
    public static bool TryNormalise(string? presented, out string code)
    {
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(presented))
        {
            return false;
        }

        var characters = new char[Length];
        var written = 0;

        foreach (var raw in presented)
        {
            if (raw is ' ' or '-')
            {
                continue;
            }

            if (written == Length)
            {
                // Longer than a code, so it is not one. Refused rather than
                // truncated: a scanner that read one character too many must
                // not silently check in whoever the prefix happens to match.
                return false;
            }

            var upper = char.ToUpperInvariant(raw);
            var folded = upper switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                _ => upper,
            };

            if (!Alphabet.Contains(folded, StringComparison.Ordinal))
            {
                return false;
            }

            characters[written++] = folded;
        }

        if (written != Length)
        {
            return false;
        }

        code = new string(characters);
        return true;
    }

    /// <summary>
    /// The code as it is shown: three groups of four.
    /// </summary>
    /// <remarks>
    /// Here rather than in the portal's stylesheet because the grouping is
    /// part of the format. Somebody reading twelve characters aloud in one
    /// breath loses their place; somebody reading three groups of four does
    /// not, and both ends of that conversation have to agree on where the gaps
    /// fall.
    /// </remarks>
    public static string Format(string code)
    {
        var groups = new List<string>(Length / GroupSize);
        for (var i = 0; i < code.Length; i += GroupSize)
        {
            groups.Add(code.Substring(i, Math.Min(GroupSize, code.Length - i)));
        }

        return string.Join(' ', groups);
    }

    /// <summary>
    /// The statuses that have a code at all.
    /// </summary>
    /// <remarks>
    /// Confirmed, because a code is only useful to somebody who said they are
    /// coming, and checked in, because taking somebody's code away the moment
    /// they used it would leave them staring at an empty screen wondering
    /// whether the scan worked.
    /// </remarks>
    public static readonly IReadOnlySet<ApplicationStatus> Issued =
        new HashSet<ApplicationStatus>
        {
            ApplicationStatus.Confirmed,
            ApplicationStatus.CheckedIn,
        };

    /// <summary>The stored spellings of <see cref="Issued"/>, for a SQL predicate.</summary>
    public static string[] IssuedWire { get; } = [.. Issued.Select(s => s.ToWire())];
}
