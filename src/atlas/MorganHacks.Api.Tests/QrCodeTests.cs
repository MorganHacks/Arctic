namespace MorganHacks.Api.Tests;

/// <summary>
/// The hand-written QR encoder.
/// </summary>
/// <remarks>
/// There is no scanner in this repository and no reference implementation to
/// compare against, so these tests do the next best thing: they take the
/// symbol apart the way a reader would. The format information is recovered
/// and checked against its own error-correcting code, the whole twenty-six
/// codewords are divided by the Reed-Solomon generator and must leave nothing,
/// and the data is unmasked, unwound from the zigzag and decoded back to the
/// string that went in.
/// <para>
/// The Galois field arithmetic here is written a different way from the
/// encoder's on purpose — logarithm tables against repeated doubling — so that
/// the check is not the same mistake twice.
/// </para>
/// <para>
/// None of which proves a phone will read it. That is why the check-in code is
/// printed underneath the symbol on the portal screen: the fallback is the
/// test this file cannot run.
/// </para>
/// </remarks>
public class QrCodeTests
{
    private const string Sample = "K7QM4XPT9BD2";

    [Fact]
    public void A_symbol_is_twenty_one_modules_square()
    {
        var symbol = QrCode.Encode(Sample);

        Assert.Equal(21, symbol.Size);
        Assert.Equal(21, symbol.Rows.Count);
        Assert.All(symbol.Rows, row => Assert.Equal(21, row.Length));
        Assert.All(symbol.Rows, row => Assert.All(row, c => Assert.True(c is '0' or '1')));
    }

    [Fact]
    public void The_three_finder_patterns_are_where_a_reader_looks_for_them()
    {
        var modules = Modules(QrCode.Encode(Sample));

        foreach (var (row, col) in new[] { (3, 3), (3, 17), (17, 3) })
        {
            for (var dr = -4; dr <= 4; dr++)
            {
                for (var dc = -4; dc <= 4; dc++)
                {
                    var r = row + dr;
                    var c = col + dc;
                    if (r is < 0 or > 20 || c is < 0 or > 20)
                    {
                        continue;
                    }

                    var ring = Math.Max(Math.Abs(dr), Math.Abs(dc));
                    Assert.Equal(ring != 2 && ring != 4, modules[r, c]);
                }
            }
        }
    }

    [Fact]
    public void The_timing_patterns_alternate_and_the_dark_module_is_dark()
    {
        var modules = Modules(QrCode.Encode(Sample));

        // Between the finders, which is where the alternation is the reader's
        // only measure of how wide a module is.
        for (var i = 8; i <= 12; i++)
        {
            Assert.Equal(i % 2 == 0, modules[6, i]);
            Assert.Equal(i % 2 == 0, modules[i, 6]);
        }

        Assert.True(modules[13, 8]);
    }

    [Fact]
    public void Both_copies_of_the_format_information_say_level_Q_and_the_same_mask()
    {
        var modules = Modules(QrCode.Encode(Sample));

        var first = FirstFormatCopy(modules);
        var second = SecondFormatCopy(modules);

        Assert.Equal(first, second);

        // A valid format codeword divides by the BCH generator with nothing
        // left over. Checking that rather than the bits themselves is what
        // catches an off-by-one in the placement, which would otherwise look
        // like a perfectly plausible number.
        var value = first ^ 0x5412;
        Assert.Equal(0, BchRemainder(value));

        Assert.Equal(0b11, value >> 13);
    }

    [Fact]
    public void The_error_correction_codewords_actually_correct_this_message()
    {
        // The whole symbol, data and correction together, divided by the
        // generator polynomial. Any message a reader can recover leaves a zero
        // remainder; this is the property the correction codewords exist to
        // create, and it fails loudly if the field arithmetic is wrong.
        var codewords = Codewords(Modules(QrCode.Encode(Sample)));

        Assert.Equal(26, codewords.Length);
        Assert.All(Remainder(codewords), b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData("K7QM4XPT9BD2")]
    [InlineData("0")]
    [InlineData("0123456789ABCDEF")]
    [InlineData("ZZZZZZZZZZZ")]
    public void A_symbol_decodes_back_to_what_went_into_it(string text)
    {
        // The odd lengths matter. Alphanumeric mode packs characters in pairs
        // at eleven bits and a leftover single at six, so a string of odd
        // length exercises a branch an even one never reaches.
        Assert.Equal(text, Decode(QrCode.Encode(text)));
    }

    [Fact]
    public void Every_code_this_system_issues_fits_in_one_symbol()
    {
        // The point of choosing Crockford base32 for the check-in code. If it
        // ever stops fitting, the symbol has to grow a version and this is
        // where that gets noticed rather than at a door.
        for (var i = 0; i < 50; i++)
        {
            var code = MorganHacks.Applications.Domain.CheckInCode.Issue();
            Assert.Equal(code, Decode(QrCode.Encode(code)));
        }
    }

    [Fact]
    public void Anything_this_version_cannot_carry_is_refused_rather_than_mangled()
    {
        Assert.Throws<ArgumentException>(() => QrCode.Encode("0123456789ABCDEFG"));
        Assert.Throws<ArgumentException>(() => QrCode.Encode("lowercase"));
        Assert.Throws<ArgumentException>(() => QrCode.Encode(string.Empty));
    }

    // -------------------------------------------------------------- reading ---

    private static bool[,] Modules(QrSymbol symbol)
    {
        var modules = new bool[symbol.Size, symbol.Size];
        for (var row = 0; row < symbol.Size; row++)
        {
            for (var col = 0; col < symbol.Size; col++)
            {
                modules[row, col] = symbol.Rows[row][col] == '1';
            }
        }

        return modules;
    }

    /// <summary>
    /// Which modules a version 1 symbol spends on structure rather than data.
    /// </summary>
    /// <remarks>
    /// Worked out from the geometry rather than copied from the encoder: the
    /// nine by nine block under each finder, and the two timing lines. It comes
    /// to two hundred and thirty-three, leaving two hundred and eight, which is
    /// twenty-six codewords exactly.
    /// </remarks>
    private static bool IsFunction(int row, int col) =>
        (row <= 8 && col <= 8)
        || (row <= 8 && col >= 13)
        || (row >= 13 && col <= 8)
        || row == 6
        || col == 6;

    private static int FirstFormatCopy(bool[,] modules)
    {
        var bits = 0;
        for (var i = 0; i <= 5; i++)
        {
            bits |= Set(modules[i, 8], i);
        }

        bits |= Set(modules[7, 8], 6);
        bits |= Set(modules[8, 8], 7);
        bits |= Set(modules[8, 7], 8);

        for (var i = 9; i < 15; i++)
        {
            bits |= Set(modules[8, 14 - i], i);
        }

        return bits;
    }

    private static int SecondFormatCopy(bool[,] modules)
    {
        var bits = 0;
        for (var i = 0; i < 8; i++)
        {
            bits |= Set(modules[8, 20 - i], i);
        }

        for (var i = 8; i < 15; i++)
        {
            bits |= Set(modules[6 + i, 8], i);
        }

        return bits;
    }

    private static int Set(bool dark, int position) => dark ? 1 << position : 0;

    private static int BchRemainder(int value)
    {
        var remainder = value;
        for (var i = 14; i >= 10; i--)
        {
            if (((remainder >> i) & 1) != 0)
            {
                remainder ^= 0x537 << (i - 10);
            }
        }

        return remainder;
    }

    /// <summary>Unmasks the symbol and reads the codewords back out of the zigzag.</summary>
    private static byte[] Codewords(bool[,] modules)
    {
        var mask = ((FirstFormatCopy(modules) ^ 0x5412) >> 10) & 0b111;

        var bits = new List<bool>(208);
        for (var right = 20; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;
            }

            for (var vertical = 0; vertical < 21; vertical++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var col = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var row = upward ? 20 - vertical : vertical;

                    if (!IsFunction(row, col))
                    {
                        bits.Add(modules[row, col] ^ Masked(row, col, mask));
                    }
                }
            }
        }

        var codewords = new byte[bits.Count / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                codewords[i / 8] |= (byte)(1 << (7 - (i % 8)));
            }
        }

        return codewords;
    }

    private static bool Masked(int row, int col, int mask) => mask switch
    {
        0 => (row + col) % 2 == 0,
        1 => row % 2 == 0,
        2 => col % 3 == 0,
        3 => (row + col) % 3 == 0,
        4 => ((row / 2) + (col / 3)) % 2 == 0,
        5 => (row * col % 2) + (row * col % 3) == 0,
        6 => (((row * col) % 2) + ((row * col) % 3)) % 2 == 0,
        7 => (((row + col) % 2) + ((row * col) % 3)) % 2 == 0,
        _ => throw new ArgumentOutOfRangeException(nameof(mask)),
    };

    private static string Decode(QrSymbol symbol)
    {
        var codewords = Codewords(Modules(symbol));
        var bits = new List<bool>(codewords.Length * 8);
        foreach (var codeword in codewords)
        {
            for (var i = 7; i >= 0; i--)
            {
                bits.Add(((codeword >> i) & 1) != 0);
            }
        }

        var read = 0;
        Assert.Equal(0b0010, Take(bits, ref read, 4));

        var count = Take(bits, ref read, 9);
        var text = new System.Text.StringBuilder(count);

        const string alphanumeric = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
        for (var i = 0; i + 1 < count; i += 2)
        {
            var pair = Take(bits, ref read, 11);
            text.Append(alphanumeric[pair / 45]).Append(alphanumeric[pair % 45]);
        }

        if (count % 2 == 1)
        {
            text.Append(alphanumeric[Take(bits, ref read, 6)]);
        }

        return text.ToString();
    }

    private static int Take(List<bool> bits, ref int position, int width)
    {
        var value = 0;
        for (var i = 0; i < width; i++)
        {
            value = (value << 1) | (bits[position++] ? 1 : 0);
        }

        return value;
    }

    // ------------------------------------------------------- Reed-Solomon ---

    /// <summary>
    /// The remainder of the whole symbol divided by the generator polynomial.
    /// </summary>
    /// <remarks>
    /// Logarithm tables rather than the encoder's repeated doubling, so this
    /// is a second opinion rather than an echo.
    /// </remarks>
    private static byte[] Remainder(byte[] codewords)
    {
        var exponent = new int[512];
        var logarithm = new int[256];
        var value = 1;
        for (var i = 0; i < 255; i++)
        {
            exponent[i] = value;
            logarithm[value] = i;
            value <<= 1;
            if ((value & 0x100) != 0)
            {
                value ^= 0x11D;
            }
        }

        for (var i = 255; i < 512; i++)
        {
            exponent[i] = exponent[i - 255];
        }

        int Multiply(int a, int b) =>
            a == 0 || b == 0 ? 0 : exponent[logarithm[a] + logarithm[b]];

        // g(x) = (x - a^0)(x - a^1)...(x - a^12), highest degree first.
        var generator = new int[] { 1 };
        for (var i = 0; i < 13; i++)
        {
            var next = new int[generator.Length + 1];
            for (var j = 0; j < generator.Length; j++)
            {
                next[j] ^= generator[j];
                next[j + 1] ^= Multiply(generator[j], exponent[i]);
            }

            generator = next;
        }

        var remainder = codewords.Select(b => (int)b).ToArray();
        for (var i = 0; i < codewords.Length - 13; i++)
        {
            var coefficient = remainder[i];
            if (coefficient == 0)
            {
                continue;
            }

            for (var j = 0; j < generator.Length; j++)
            {
                remainder[i + j] ^= Multiply(generator[j], coefficient);
            }
        }

        return [.. remainder[^13..].Select(b => (byte)b)];
    }
}
