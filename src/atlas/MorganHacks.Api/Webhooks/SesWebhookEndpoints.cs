using System.Text.Json;
using System.Text.Json.Serialization;
using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api.Webhooks;

/// <summary>
/// Where SES tells us what happened to mail we sent.
/// </summary>
/// <remarks>
/// This lives in atlas rather than lark on purpose. Lark is a worker with no
/// ingress — giving it an HTTP surface means a port, a health check, a public
/// route and one more thing exposed, all to receive a webhook. Atlas already
/// has all of that, and already queues through lark's own API, so this handler
/// does the same and lark stays a worker.
/// <para>
/// Without this endpoint the bounce rate climbs invisibly until SES suspends
/// the account, which would happen during registration week and look like
/// nothing at all until it did.
/// </para>
/// </remarks>
public static class SesWebhookEndpoints
{
    public static IEndpointRouteBuilder MapSesWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/ses", Handle).RequireRateLimiting("webhook");
        return app;
    }

    private sealed class SesEvent
    {
        [JsonPropertyName("eventType")] public string? EventType { get; set; }
        [JsonPropertyName("notificationType")] public string? NotificationType { get; set; }
        [JsonPropertyName("mail")] public SesMail? Mail { get; set; }
        [JsonPropertyName("bounce")] public SesBounce? Bounce { get; set; }
        [JsonPropertyName("complaint")] public SesComplaint? Complaint { get; set; }

        /// <summary>SES uses one name in one delivery mode and the other elsewhere.</summary>
        public string? Kind => EventType ?? NotificationType;
    }

    private sealed class SesMail
    {
        [JsonPropertyName("messageId")] public string? MessageId { get; set; }
    }

    private sealed class SesBounce
    {
        [JsonPropertyName("bounceType")] public string? BounceType { get; set; }
        [JsonPropertyName("bounceSubType")] public string? BounceSubType { get; set; }
    }

    private sealed class SesComplaint
    {
        [JsonPropertyName("complaintFeedbackType")] public string? FeedbackType { get; set; }
    }

    private static async Task<IResult> Handle(
        HttpContext http,
        ISnsSignatureVerifier verifier,
        MessageQueue queue,
        IHttpClientFactory clients,
        ILogger<SnsMessage> log,
        CancellationToken ct)
    {
        // SNS posts with Content-Type text/plain, so the body is read directly
        // rather than model-bound.
        using var reader = new StreamReader(http.Request.Body);
        var body = await reader.ReadToEndAsync(ct);

        SnsMessage? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SnsMessage>(body);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (envelope is null || !await verifier.IsAuthenticAsync(envelope, ct))
        {
            // 403 and nothing else. An unverified caller learns only that it
            // was refused, never whether an address or message existed.
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (envelope.IsSubscriptionConfirmation)
        {
            // Confirmed by fetching the URL AWS sent, which is only reached
            // after the signature checked out — otherwise this endpoint would
            // fetch any URL a stranger put in a request body.
            if (SnsSignatureVerifier.IsTrustedCertUrl(envelope.SubscribeUrl)
                || IsSnsUrl(envelope.SubscribeUrl))
            {
                using var client = clients.CreateClient();
                await client.GetAsync(envelope.SubscribeUrl, ct);
                log.LogInformation("Confirmed an SNS subscription.");
            }

            return Results.Ok();
        }

        if (!envelope.IsNotification || string.IsNullOrEmpty(envelope.Message))
        {
            return Results.Ok();
        }

        SesEvent? notification;
        try
        {
            notification = JsonSerializer.Deserialize<SesEvent>(envelope.Message);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        var providerMessageId = notification?.Mail?.MessageId;
        if (notification is null || string.IsNullOrEmpty(providerMessageId))
        {
            return Results.Ok();
        }

        await ApplyAsync(notification, providerMessageId, queue, log, ct);

        // Always 200 once it is genuinely from AWS. A non-2xx makes SNS retry,
        // and retrying an event we have already applied, or one about a
        // message we do not recognise, achieves nothing but noise.
        return Results.Ok();
    }

    private static async Task ApplyAsync(
        SesEvent notification,
        string providerMessageId,
        MessageQueue queue,
        ILogger log,
        CancellationToken ct)
    {
        switch (notification.Kind)
        {
            case "Delivery":
                await queue.MarkDeliveredAsync(providerMessageId, ct);
                break;

            case "Bounce":
                var permanent = notification.Bounce?.BounceType == "Permanent";
                await queue.MarkBouncedAsync(
                    providerMessageId,
                    $"{notification.Bounce?.BounceType}/{notification.Bounce?.BounceSubType}",
                    ct);

                // Only a permanent bounce suppresses. A transient one is a full
                // mailbox or a server having a bad day, and suppressing on that
                // would lock somebody out over a temporary problem.
                if (permanent && await queue.RecipientOfAsync(providerMessageId, ct) is { } address)
                {
                    await queue.SuppressAsync(address, "hard_bounce", ct);
                    log.LogInformation("Suppressed an address after a permanent bounce.");
                }

                break;

            case "Complaint":
                // Instant and permanent, on the first one. Somebody pressed
                // "this is spam"; there is no grace period to give, and
                // continuing to mail them is what gets the domain blocked.
                await queue.MarkComplainedAsync(
                    providerMessageId,
                    notification.Complaint?.FeedbackType ?? "complaint", ct);

                if (await queue.RecipientOfAsync(providerMessageId, ct) is { } complainer)
                {
                    await queue.SuppressAsync(complainer, "complaint", ct);
                    log.LogWarning("Suppressed an address after a complaint.");
                }

                break;

            default:
                log.LogInformation(
                    "Ignored an SES event of type {Kind}.", notification.Kind);
                break;
        }
    }

    private static bool IsSnsUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase);
}
