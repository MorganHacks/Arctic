using System.Text.RegularExpressions;

namespace MorganHacks.Applications.Forms;

public sealed record FormTheme(
    string Accent = "#003970",
    string Background = "white",
    string Font = "sans",
    string Size = "medium",
    string? HeaderImage = null,
    bool ShowMlhBadge = false,
    FormLinkCard? LinkCard = null,
    string MlhBadgeColor = "white")
{
    public static FormTheme Default { get; } = new();

    public bool IsValid() =>
        Accent is not null && Regex.IsMatch(Accent, "^#[0-9a-fA-F]{6}$")
        && (Background is "neutral" or "tint" or "white"
            || Background is not null && Regex.IsMatch(Background, "^#[0-9a-fA-F]{6}$"))
        && Font is "sans" or "serif" or "mono"
        && Size is "small" or "medium" or "large"
        && ValidHeaderImage(HeaderImage)
        && MlhBadgeColor is "auto" or "white" or "black" or "gray" or "red" or "blue" or "yellow"
        && (LinkCard is null || LinkCard.IsValid());

    internal static bool ValidHeaderImage(string? image)
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

public sealed record FormLinkCard(string Label = "", string Title = "", string Url = "", string? Image = null)
{
    public bool IsValid() =>
        Label is not null && Label.Length <= 60
        && !string.IsNullOrWhiteSpace(Title) && Title.Length <= 80
        && Url is not null && Url.Length <= 2048
        && Regex.IsMatch(Url, "^https?://", RegexOptions.IgnoreCase)
        && !Url.Any(c => char.IsWhiteSpace(c) || c == '\\')
        && Uri.TryCreate(Url, UriKind.Absolute, out var uri)
        && !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo)
        && (Image is null || Image.Length <= 80_000 && FormTheme.ValidHeaderImage(Image));
}
