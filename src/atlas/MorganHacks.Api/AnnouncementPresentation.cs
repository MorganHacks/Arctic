using MorganHacks.Applications.Services;

namespace MorganHacks.Api;

internal static class AnnouncementPresentation
{
    public static object? Results(AnnouncementContent? content, AnnouncementTally? tally, bool organizer = false)
    {
        if (content?.IsQuestion != true) return null;
        tally ??= new AnnouncementTally(new int[4], null);
        return new
        {
            total = tally.Total,
            counts = organizer || tally.Choice is not null ? tally.Counts.Take(content.Options!.Length).ToArray() : null,
            choice = tally.Choice,
        };
    }

    public static AnnouncementContent? ForApplicant(AnnouncementContent? content, AnnouncementTally? tally) =>
        content?.Kind == "quiz" && tally?.Choice is null
            ? content with { CorrectOption = null, Explanation = null }
            : content;
}
