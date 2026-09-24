namespace MorganHacks.Lark.Data.Domain;

public static class EmailUnsubscribe
{
    public const string Placeholder = "{$unsubscribe_link}";

    public static bool IsNeeded(ClaimedMessage message) =>
        message.BodyHtml.Contains(Placeholder, StringComparison.Ordinal)
        || message.BodyText.Contains(Placeholder, StringComparison.Ordinal);

    public static string Replace(string content, string url) =>
        content.Replace(Placeholder, url, StringComparison.Ordinal);
}
