using System.Text.RegularExpressions;

namespace MorganHacks.Applications.Forms;

public sealed record FormTheme(
    string Accent = "#003970",
    string Background = "neutral",
    string Font = "sans",
    string Size = "medium",
    string? HeaderImage = null,
    bool ShowMlhBadge = false)
{
    public static FormTheme Default { get; } = new();

    public bool IsValid() =>
        Accent is not null && Regex.IsMatch(Accent, "^#[0-9a-fA-F]{6}$")
        && Background is "neutral" or "tint" or "white"
        && Font is "sans" or "serif" or "mono"
        && Size is "small" or "medium" or "large"
        && ValidHeaderImage(HeaderImage);

    private static bool ValidHeaderImage(string? image)
    {
        if (image is null) return true;
        const string prefix = "data:image/webp;base64,";
        if (image.Length > 350_000 || !image.StartsWith(prefix, StringComparison.Ordinal)) return false;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(image[prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }
        return bytes.Length >= 20
            && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)
            && bytes.AsSpan(12, 3).SequenceEqual("VP8"u8);
    }
}
