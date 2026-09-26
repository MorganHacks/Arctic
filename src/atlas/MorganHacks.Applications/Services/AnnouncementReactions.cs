namespace MorganHacks.Applications.Services;

public sealed record AnnouncementReactions(Dictionary<string, int> Counts, string? Choice)
{
    public int Total => Counts.Values.Sum();

    public static bool IsValid(string reaction) => reaction is "love" or "wow" or "confused" or "support" or "happy";

    public static AnnouncementReactions Empty() => new(new()
    {
        ["love"] = 0,
        ["wow"] = 0,
        ["confused"] = 0,
        ["support"] = 0,
        ["happy"] = 0,
    }, null);
}
