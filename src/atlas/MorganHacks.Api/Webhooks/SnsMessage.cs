using System.Text;
using System.Text.Json.Serialization;

namespace MorganHacks.Api.Webhooks;

/// <summary>An SNS envelope, as posted to the webhook.</summary>
public sealed class SnsMessage
{
    [JsonPropertyName("Type")] public string? Type { get; set; }
    [JsonPropertyName("MessageId")] public string? MessageId { get; set; }
    [JsonPropertyName("TopicArn")] public string? TopicArn { get; set; }
    [JsonPropertyName("Subject")] public string? Subject { get; set; }
    [JsonPropertyName("Message")] public string? Message { get; set; }
    [JsonPropertyName("Timestamp")] public string? Timestamp { get; set; }
    [JsonPropertyName("SignatureVersion")] public string? SignatureVersion { get; set; }
    [JsonPropertyName("Signature")] public string? Signature { get; set; }
    [JsonPropertyName("SigningCertURL")] public string? SigningCertUrl { get; set; }
    [JsonPropertyName("SubscribeURL")] public string? SubscribeUrl { get; set; }
    [JsonPropertyName("Token")] public string? Token { get; set; }

    public bool IsNotification => Type == "Notification";
    public bool IsSubscriptionConfirmation => Type == "SubscriptionConfirmation";

    /// <summary>
    /// The exact bytes AWS signed.
    /// </summary>
    /// <remarks>
    /// Field order and the set of included fields are fixed by AWS and differ
    /// by message type. Getting either wrong does not produce a subtly weaker
    /// check — it produces a signature that never verifies, so this fails
    /// closed rather than open.
    /// <para>
    /// Only these fields are signed. Anything else in the body is unsigned and
    /// must never be trusted, which is why the handler reads the payload out
    /// of <c>Message</c> and nowhere else.
    /// </para>
    /// </remarks>
    public byte[]? CanonicalBytes()
    {
        string[] fields = Type switch
        {
            "Notification" => Subject is null
                ? ["Message", "MessageId", "Timestamp", "TopicArn", "Type"]
                : ["Message", "MessageId", "Subject", "Timestamp", "TopicArn", "Type"],
            "SubscriptionConfirmation" or "UnsubscribeConfirmation" =>
                ["Message", "MessageId", "SubscribeURL", "Timestamp", "Token", "TopicArn", "Type"],
            _ => [],
        };

        if (fields.Length == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var field in fields)
        {
            var value = field switch
            {
                "Message" => Message,
                "MessageId" => MessageId,
                "Subject" => Subject,
                "SubscribeURL" => SubscribeUrl,
                "Timestamp" => Timestamp,
                "Token" => Token,
                "TopicArn" => TopicArn,
                "Type" => Type,
                _ => null,
            };

            if (value is null)
            {
                return null;
            }

            builder.Append(field).Append('\n').Append(value).Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
