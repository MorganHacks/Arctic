using System.Text.Json;
using System.Text.Json.Serialization;

namespace MorganHacks.Applications.Services;

public sealed record AnnouncementMedia(string Url, string? Alt = null);
public sealed record AnnouncementOption(string Text, string? ImageUrl = null);
public sealed record AnnouncementContent(
    string Kind,
    AnnouncementMedia[]? Media = null,
    AnnouncementOption[]? Options = null,
    int? CorrectOption = null,
    string? Explanation = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [JsonIgnore]
    public bool IsQuestion => Kind is "poll" or "imagePoll" or "quiz";

    public string? Validate()
    {
        if (Kind is not ("image" or "video" or "poll" or "imagePoll" or "quiz"))
            return "Choose an image, video, poll or quiz.";
        if (Kind is "image" or "video")
        {
            if (Media is null || Media.Length == 0 || Media.Length > (Kind == "video" ? 1 : 4))
                return Kind == "video" ? "Add one video link." : "Add between one and four images.";
            if (Media.Any(m => m is null || !IsMediaUrl(m.Url) || m.Alt?.Length > 200))
                return "Use a valid HTTPS media link and keep image descriptions under 200 characters.";
            if (Options is { Length: > 0 } || CorrectOption is not null || Explanation is not null)
                return "Media posts cannot include poll or quiz answers.";
        }
        else
        {
            if (Options is null || Options.Length is < 2 or > 4)
                return "Add between two and four choices.";
            if (Options.Any(o => o is null || string.IsNullOrWhiteSpace(o.Text) || o.Text.Trim().Length > 100))
                return "Give each choice a label of up to 100 characters.";
            if (Options.Select(o => o.Text.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Options.Length)
                return "Each choice needs a different label.";
            if (Kind == "imagePoll" && Options.Any(o => !IsMediaUrl(o.ImageUrl)))
                return "Add an HTTPS image link for every choice.";
            if (Kind != "imagePoll" && Options.Any(o => o.ImageUrl is not null))
                return "Use an image poll to attach images to choices.";
            if (Media is { Length: > 0 }) return "Attach images to the choices instead.";
            if (Kind == "quiz")
            {
                if (CorrectOption is null || CorrectOption < 0 || CorrectOption >= Options.Length)
                    return "Choose the correct quiz answer.";
                if (Explanation?.Length > 280) return "Keep the answer explanation under 280 characters.";
            }
            else if (CorrectOption is not null || Explanation is not null)
                return "Correct answers and explanations belong to quizzes.";
        }
        return null;
    }

    public static bool IsMediaUrl(string? value) =>
        value is { Length: > 0 and <= 2048 }
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(uri.Host)
        && uri.UserInfo.Length == 0;
}

public sealed record AnnouncementTally(int[] Counts, int? Choice)
{
    public int Total => Counts.Sum();
}

public sealed record AnnouncementVoteResult(AnnouncementContent? Content, string? Error, int StatusCode);
