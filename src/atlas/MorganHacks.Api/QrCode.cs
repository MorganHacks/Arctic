namespace MorganHacks.Api;

/// <summary>
/// A finished QR symbol as rows of modules.
/// </summary>
/// <remarks>
/// Rows of <c>'0'</c> and <c>'1'</c> rather than an image. The API's job is to
/// say which modules are dark; how large they are drawn, what they are drawn
/// with and how much quiet zone sits around them are the portal's, and a
/// server that shipped an SVG would be deciding all three from here.
/// <para>
/// One character per module also keeps this small enough not to think about:
/// twenty-one strings of twenty-one characters, next to a response that
/// already carries several sentences of English.
/// </para>
/// </remarks>
public sealed record QrSymbol(int Size, IReadOnlyList<string> Rows);

/// <summary>
/// A QR encoder, narrowed to exactly the symbol this codebase needs.
/// </summary>
/// <remarks>
/// Version 1, error correction level Q, alphanumeric mode. Twenty-one modules
/// square, up to sixteen characters, no alignment patterns and no version
/// block. That is the whole standard that applies at this size, which is why
/// writing it was preferable to taking a dependency: a general encoder is
/// several thousand lines and a table of capacities for forty versions, and
/// none of it would ever run.
/// <para>
/// Level Q rather than the more usual M. Both are twenty-one modules across at
/// this length, so the higher redundancy is free: it costs nothing in size and
/// buys back a quarter of the symbol, which is roughly the fraction a thumb
/// covers on a phone held out at arm's length.
/// </para>
/// <para>
/// The check-in code is shown as text underneath the symbol on purpose, and
/// this is part of the reason. A hand-written encoder is a thing that can be
/// wrong in a way no test here would catch, and the answer to that is a code a
/// volunteer can read out loud, not confidence.
/// </para>
/// </remarks>
public static class QrCode
{
    /// <summary>Modules across. 4 x version + 17.</summary>
    public const int Size = 21;

    /// <summary>
    /// The longest string this will encode.
    /// </summary>
    /// <remarks>
    /// Version 1 at level Q holds thirteen data codewords, which is one
    /// hundred and four bits. Four for the mode, nine for the count, and
    /// eleven per pair of characters gives sixteen. The check-in code is
    /// twelve.
    /// </remarks>
    public const int MaxLength = 16;

    /// <summary>
    /// The characters QR calls alphanumeric. Position in this string is the value.
    /// </summary>
    /// <remarks>
    /// Crockford base32 is a subset of it, which is why the check-in code fits
    /// in a twenty-one module symbol at all. Byte mode would need eight bits a
    /// character instead of five and a half, and twelve characters would no
    /// longer fit at this error correction level.
    /// </remarks>
    private const string Alphanumeric = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    /// <summary>Total codewords in a version 1 symbol.</summary>
    private const int TotalCodewords = 26;

    /// <summary>Of which thirteen carry data at level Q, and thirteen correct it.</summary>
    private const int DataCodewords = 13;

    /// <summary>Level Q, as the two bits that go in the format information.</summary>
    private const int LevelQ = 0b11;

    /// <summary>The alphanumeric mode indicator.</summary>
    private const int AlphanumericMode = 0b0010;

    /// <summary>Character count bits for alphanumeric mode in versions 1 to 9.</summary>
    private const int CountBits = 9;

    /// <summary>
    /// Encodes a string, choosing the mask that scans best.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The string is empty, longer than <see cref="MaxLength"/>, or holds a
    /// character alphanumeric mode cannot carry. Thrown rather than silently
    /// falling back to a larger symbol, because there is no caller here that
    /// should ever be passing one.
    /// </exception>
    public static QrSymbol Encode(string text)
    {
        var codewords = ToCodewords(text);

        var modules = new bool[Size, Size];
        var isFunction = new bool[Size, Size];
        DrawFunctionPatterns(modules, isFunction);
        DrawCodewords(codewords, modules, isFunction);

        // Every mask produces a symbol a reader can decode, because the one
        // that was used is recorded in the format bits. The penalty rules are
        // about how easily: they punish large blocks of one colour and
        // sequences that look like a finder pattern, both of which make a
        // camera work harder in bad light. Masking is its own inverse, so each
        // candidate is applied, scored and applied again.
        var best = 0;
        var lowest = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            ApplyMask(modules, isFunction, mask);
            DrawFormatBits(modules, isFunction, mask);

            var penalty = Penalty(modules);
            if (penalty < lowest)
            {
                lowest = penalty;
                best = mask;
            }

            ApplyMask(modules, isFunction, mask);
        }

        ApplyMask(modules, isFunction, best);
        DrawFormatBits(modules, isFunction, best);

        var rows = new string[Size];
        for (var row = 0; row < Size; row++)
        {
            var line = new char[Size];
            for (var col = 0; col < Size; col++)
            {
                line[col] = modules[row, col] ? '1' : '0';
            }

            rows[row] = new string(line);
        }

        return new QrSymbol(Size, rows);
    }

    // ------------------------------------------------------------- the data ---

    /// <summary>The twenty-six codewords: thirteen of data, thirteen of correction.</summary>
    private static byte[] ToCodewords(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        if (text.Length > MaxLength)
        {
            throw new ArgumentException(
                $"A version 1 symbol holds {MaxLength} characters, not {text.Length}.",
                nameof(text));
        }

        var values = new int[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var value = Alphanumeric.IndexOf(text[i], StringComparison.Ordinal);
            if (value < 0)
            {
                throw new ArgumentException(
                    "Alphanumeric mode cannot carry that character.", nameof(text));
            }

            values[i] = value;
        }

        var bits = new List<bool>(DataCodewords * 8);
        Append(bits, AlphanumericMode, 4);
        Append(bits, text.Length, CountBits);

        // Pairs at eleven bits, because forty-five squared is under two
        // thousand and forty-eight. A trailing odd character takes six.
        var index = 0;
        for (; index + 1 < values.Length; index += 2)
        {
            Append(bits, (values[index] * 45) + values[index + 1], 11);
        }

        if (index < values.Length)
        {
            Append(bits, values[index], 6);
        }

        // Terminator, then out to a whole byte, then the two pad codewords the
        // standard names, alternating until the data half is full.
        var capacity = DataCodewords * 8;
        for (var i = 0; i < 4 && bits.Count < capacity; i++)
        {
            bits.Add(false);
        }

        while (bits.Count % 8 != 0)
        {
            bits.Add(false);
        }

        for (var pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11)
        {
            Append(bits, pad, 8);
        }

        var codewords = new byte[TotalCodewords];
        for (var i = 0; i < capacity; i++)
        {
            if (bits[i])
            {
                codewords[i / 8] |= (byte)(1 << (7 - (i % 8)));
            }
        }

        // One block at this version, so the correction codewords simply follow
        // the data. Interleaving only exists from version 3 upwards.
        var data = codewords.AsSpan(0, DataCodewords).ToArray();
        Remainder(data, Generator(TotalCodewords - DataCodewords))
            .CopyTo(codewords, DataCodewords);

        return codewords;
    }

    private static void Append(List<bool> bits, int value, int width)
    {
        for (var i = width - 1; i >= 0; i--)
        {
            bits.Add(((value >> i) & 1) != 0);
        }
    }

    // -------------------------------------------------------- Reed-Solomon ---

    /// <summary>
    /// The generator polynomial for a given number of correction codewords.
    /// </summary>
    /// <remarks>
    /// Computed rather than tabulated. The published tables are the product of
    /// this loop, and a table is a place for a typo that nothing detects until
    /// a scanner in a doorway refuses a code.
    /// </remarks>
    private static byte[] Generator(int degree)
    {
        var coefficients = new byte[degree];
        coefficients[degree - 1] = 1;

        // Multiply out (x - a^0)(x - a^1)... over GF(256). Subtraction is XOR,
        // so the sign never appears.
        var root = (byte)1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                coefficients[j] = Multiply(coefficients[j], root);
                if (j + 1 < degree)
                {
                    coefficients[j] ^= coefficients[j + 1];
                }
            }

            root = Multiply(root, 2);
        }

        return coefficients;
    }

    /// <summary>The remainder of the data divided by the generator.</summary>
    private static byte[] Remainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];

        foreach (var codeword in data)
        {
            var factor = (byte)(codeword ^ result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;

            for (var i = 0; i < result.Length; i++)
            {
                result[i] ^= Multiply(divisor[i], factor);
            }
        }

        return result;
    }

    /// <summary>
    /// Multiplication in GF(256) with the QR field polynomial.
    /// </summary>
    /// <remarks>
    /// 0x11D is the primitive polynomial the standard names. Done the long way
    /// rather than through logarithm tables: this runs a few thousand times per
    /// symbol, which is nothing, and two tables that have to agree with each
    /// other is two places to be wrong.
    /// </remarks>
    private static byte Multiply(byte left, byte right)
    {
        var product = 0;
        for (var i = 7; i >= 0; i--)
        {
            product = (product << 1) ^ ((product >> 7) * 0x11D);
            product ^= ((right >> i) & 1) * left;
        }

        return (byte)product;
    }

    // ---------------------------------------------------- the symbol itself ---

    private static void Set(bool[,] modules, bool[,] isFunction, int row, int col, bool dark)
    {
        modules[row, col] = dark;
        isFunction[row, col] = true;
    }

    private static void DrawFunctionPatterns(bool[,] modules, bool[,] isFunction)
    {
        // Timing patterns, the alternating line a reader measures the module
        // size against. Drawn across the whole width first; the finders
        // overwrite both ends.
        for (var i = 0; i < Size; i++)
        {
            Set(modules, isFunction, 6, i, i % 2 == 0);
            Set(modules, isFunction, i, 6, i % 2 == 0);
        }

        DrawFinder(modules, isFunction, 3, 3);
        DrawFinder(modules, isFunction, 3, Size - 4);
        DrawFinder(modules, isFunction, Size - 4, 3);

        // Reserved with a throwaway mask, so the data placement below knows to
        // step over it. The real bits go on once a mask has been chosen.
        DrawFormatBits(modules, isFunction, 0);
    }

    /// <summary>
    /// One finder pattern and the light separator around it.
    /// </summary>
    /// <remarks>
    /// Written as rings out from the centre because that is what the pattern
    /// is: a dark three by three, a light ring, a dark ring, and then the
    /// separator, which is a light ring that only exists to stop the pattern
    /// touching the data.
    /// </remarks>
    private static void DrawFinder(bool[,] modules, bool[,] isFunction, int row, int col)
    {
        for (var dr = -4; dr <= 4; dr++)
        {
            for (var dc = -4; dc <= 4; dc++)
            {
                var ring = Math.Max(Math.Abs(dr), Math.Abs(dc));
                var r = row + dr;
                var c = col + dc;

                if (r >= 0 && r < Size && c >= 0 && c < Size)
                {
                    Set(modules, isFunction, r, c, ring != 2 && ring != 4);
                }
            }
        }
    }

    /// <summary>
    /// The fifteen format bits, twice, plus the module that is always dark.
    /// </summary>
    /// <remarks>
    /// Two copies because they are the only thing in the symbol that says
    /// which error correction level and which mask were used, and a reader
    /// that cannot recover them cannot recover anything else either. The
    /// second copy is placed so that the two never share a damaged region.
    /// </remarks>
    private static void DrawFormatBits(bool[,] modules, bool[,] isFunction, int mask)
    {
        var value = (LevelQ << 3) | mask;

        // BCH(15, 5), then XOR with the standard's mask so that an all-zero
        // format never produces an all-light corner.
        var remainder = value;
        for (var i = 0; i < 10; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        }

        var bits = ((value << 10) | remainder) ^ 0x5412;

        for (var i = 0; i <= 5; i++)
        {
            Set(modules, isFunction, i, 8, Bit(bits, i));
        }

        Set(modules, isFunction, 7, 8, Bit(bits, 6));
        Set(modules, isFunction, 8, 8, Bit(bits, 7));
        Set(modules, isFunction, 8, 7, Bit(bits, 8));

        for (var i = 9; i < 15; i++)
        {
            Set(modules, isFunction, 8, 14 - i, Bit(bits, i));
        }

        for (var i = 0; i < 8; i++)
        {
            Set(modules, isFunction, 8, Size - 1 - i, Bit(bits, i));
        }

        for (var i = 8; i < 15; i++)
        {
            Set(modules, isFunction, Size - 15 + i, 8, Bit(bits, i));
        }

        // The one module that is dark in every symbol ever made.
        Set(modules, isFunction, Size - 8, 8, true);
    }

    private static bool Bit(int value, int position) => ((value >> position) & 1) != 0;

    /// <summary>
    /// Lays the codewords into the symbol.
    /// </summary>
    /// <remarks>
    /// Two modules wide, bottom right to top left, snaking up and down and
    /// stepping over anything already claimed by a function pattern. Column
    /// six is skipped entirely because the vertical timing pattern owns it.
    /// </remarks>
    private static void DrawCodewords(byte[] codewords, bool[,] modules, bool[,] isFunction)
    {
        var bit = 0;

        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;
            }

            for (var vertical = 0; vertical < Size; vertical++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var col = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var row = upward ? Size - 1 - vertical : vertical;

                    if (!isFunction[row, col] && bit < codewords.Length * 8)
                    {
                        modules[row, col] = Bit(codewords[bit / 8], 7 - (bit % 8));
                        bit++;
                    }
                }
            }
        }
    }

    private static void ApplyMask(bool[,] modules, bool[,] isFunction, int mask)
    {
        for (var row = 0; row < Size; row++)
        {
            for (var col = 0; col < Size; col++)
            {
                if (isFunction[row, col])
                {
                    continue;
                }

                var invert = mask switch
                {
                    0 => (row + col) % 2 == 0,
                    1 => row % 2 == 0,
                    2 => col % 3 == 0,
                    3 => (row + col) % 3 == 0,
                    4 => ((row / 2) + (col / 3)) % 2 == 0,
                    5 => (row * col % 2) + (row * col % 3) == 0,
                    6 => (((row * col) % 2) + ((row * col) % 3)) % 2 == 0,
                    7 => (((row + col) % 2) + ((row * col) % 3)) % 2 == 0,
                    _ => throw new ArgumentOutOfRangeException(nameof(mask), mask, null),
                };

                modules[row, col] ^= invert;
            }
        }
    }

    // ------------------------------------------------------------ penalties ---

    /// <summary>
    /// How badly a masked symbol scans. Lower is better.
    /// </summary>
    /// <remarks>
    /// The standard's four rules, which between them describe the things that
    /// confuse a camera: long runs of one colour, solid blocks, sequences that
    /// look like the finder pattern, and an overall balance far from half dark.
    /// </remarks>
    private static int Penalty(bool[,] modules)
    {
        var penalty = 0;

        for (var i = 0; i < Size; i++)
        {
            penalty += LinePenalty(modules, i, horizontal: true);
            penalty += LinePenalty(modules, i, horizontal: false);
        }

        // Rule two: every two by two block of one colour, counted with overlap.
        for (var row = 0; row < Size - 1; row++)
        {
            for (var col = 0; col < Size - 1; col++)
            {
                var corner = modules[row, col];
                if (modules[row, col + 1] == corner
                    && modules[row + 1, col] == corner
                    && modules[row + 1, col + 1] == corner)
                {
                    penalty += 3;
                }
            }
        }

        // Rule four: how far the proportion of dark modules is from half.
        var dark = 0;
        for (var row = 0; row < Size; row++)
        {
            for (var col = 0; col < Size; col++)
            {
                if (modules[row, col])
                {
                    dark++;
                }
            }
        }

        var away = Math.Abs((dark * 100.0 / (Size * Size)) - 50);
        penalty += (int)(away / 5) * 10;

        return penalty;
    }

    /// <summary>Rules one and three, along a single row or column.</summary>
    private static int LinePenalty(bool[,] modules, int index, bool horizontal)
    {
        var line = new bool[Size];
        for (var i = 0; i < Size; i++)
        {
            line[i] = horizontal ? modules[index, i] : modules[i, index];
        }

        var penalty = 0;

        // Rule one: five in a row scores three, and every module past that
        // scores one more.
        var run = 1;
        for (var i = 1; i < Size; i++)
        {
            if (line[i] == line[i - 1])
            {
                run++;
                continue;
            }

            if (run >= 5)
            {
                penalty += run - 2;
            }

            run = 1;
        }

        if (run >= 5)
        {
            penalty += run - 2;
        }

        // Rule three: the finder pattern's own signature, dark-light-dark-dark-
        // dark-light-dark, with four light modules on either side of it. A
        // reader that finds one of these in the data has found a corner that
        // is not there.
        for (var i = 0; i + 11 <= Size; i++)
        {
            if (Matches(line, i, FinderLike) || Matches(line, i, FinderLikeReversed))
            {
                penalty += 40;
            }
        }

        return penalty;
    }

    private static readonly bool[] FinderLike =
        [true, false, true, true, true, false, true, false, false, false, false];

    private static readonly bool[] FinderLikeReversed =
        [false, false, false, false, true, false, true, true, true, false, true];

    private static bool Matches(bool[] line, int start, bool[] pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            if (line[start + i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }
}
