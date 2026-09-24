using System.Net;
using MorganHacks.Lark.Data.Domain;
using Npgsql;

namespace MorganHacks.Lark.Data.Data;

public sealed class UnsubscribeStore(NpgsqlDataSource dataSource)
{
    public async Task<ClaimedMessage> PrepareAsync(
        ClaimedMessage message, string publicBaseUrl, CancellationToken ct = default)
    {
        if (!EmailUnsubscribe.IsNeeded(message)) return message;

        if (!EmailLinks.IsWebUrl(publicBaseUrl)
            || !Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var origin)
            || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || (origin.Scheme != "https" && !origin.IsLoopback))
        {
            throw new InvalidOperationException("Set SendLoop:UnsubscribeBaseUrl to the public API address.");
        }

        await using var command = dataSource.CreateCommand("""
            INSERT INTO notify.unsubscribe_links (email) VALUES (@email)
            ON CONFLICT (email) DO UPDATE SET email = notify.unsubscribe_links.email
            RETURNING id
            """);
        command.Parameters.AddWithValue("email", message.ToEmail);
        var id = (Guid)(await command.ExecuteScalarAsync(ct))!;
        var url = $"{publicBaseUrl.TrimEnd('/')}/email/unsubscribe/{id:N}";
        return message with
        {
            BodyHtml = EmailUnsubscribe.Replace(message.BodyHtml, WebUtility.HtmlEncode(url)),
            BodyText = EmailUnsubscribe.Replace(message.BodyText, url),
        };
    }

    public async Task<string?> FindEmailAsync(Guid id, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT email FROM notify.unsubscribe_links WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(ct) as string;
    }
}
