using System.Security.Cryptography;
using System.Text;
using MorganHacks.Lark.Data.Domain;
using Npgsql;

namespace MorganHacks.Lark.Data.Data;

public sealed class LinkTrackingStore(NpgsqlDataSource dataSource)
{
    public async Task<ClaimedMessage> PrepareAsync(
        ClaimedMessage message, string publicBaseUrl, CancellationToken ct = default)
    {
        if (!message.ClickTracking)
        {
            return message;
        }

        var destinations = EmailLinks.Destinations(message.BodyHtml);
        if (destinations.Count == 0)
        {
            return message;
        }

        if (!EmailLinks.IsWebUrl(publicBaseUrl)
            || !Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var origin)
            || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || (origin.Scheme != "https" && !origin.IsLoopback))
        {
            throw new InvalidOperationException("Set SendLoop:ClickTrackingBaseUrl to the public API address.");
        }

        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var write = new NpgsqlCommand("""
            INSERT INTO notify.tracked_links (message_id, destination, destination_hash)
            SELECT @message, link.destination, link.hash
              FROM unnest(@destinations::text[], @hashes::bytea[]) AS link(destination, hash)
            ON CONFLICT (message_id, destination_hash) DO NOTHING
            """, connection, transaction))
        {
            write.Parameters.AddWithValue("message", message.Id);
            write.Parameters.AddWithValue("destinations", destinations.ToArray());
            write.Parameters.AddWithValue("hashes", destinations
                .Select(destination => SHA256.HashData(Encoding.UTF8.GetBytes(destination))).ToArray());
            await write.ExecuteNonQueryAsync(ct);
        }
        await using (var read = new NpgsqlCommand("""
            SELECT id, destination FROM notify.tracked_links WHERE message_id = @message
            """, connection, transaction))
        {
            read.Parameters.AddWithValue("message", message.Id);
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                links[reader.GetString(1)] = $"{publicBaseUrl.TrimEnd('/')}/email/click/{reader.GetGuid(0):N}";
            }
        }
        await transaction.CommitAsync(ct);
        return message with
        {
            BodyHtml = EmailLinks.RewriteHtml(message.BodyHtml, links),
            BodyText = EmailLinks.RewriteText(message.BodyText, links),
        };
    }

    public async Task<string?> VisitAsync(Guid id, bool recordClick, CancellationToken ct = default)
    {
        var sql = recordClick
            ? """
              UPDATE notify.tracked_links
                 SET click_count = click_count + 1,
                     first_clicked_at = COALESCE(first_clicked_at, now()),
                     last_clicked_at = now()
               WHERE id = @id
              RETURNING destination
              """
            : "SELECT destination FROM notify.tracked_links WHERE id = @id";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(ct) as string;
    }
}
