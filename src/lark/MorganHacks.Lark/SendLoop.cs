using Microsoft.Extensions.Options;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;
using MorganHacks.Lark.Sending;
using MorganHacks.Observability;
using Serilog.Context;

namespace MorganHacks.Lark;

/// <summary>How the loop is tuned. All of it has a reason.</summary>
public sealed class SendLoopOptions
{
    /// <summary>Messages taken per claim.</summary>
    public int BatchSize { get; set; } = 25;

    /// <summary>How long to wait when there was nothing to do.</summary>
    public TimeSpan IdleDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gap between sends, which is a send-rate ceiling expressed the simple way.
    /// </summary>
    /// <remarks>
    /// SES gives a sustained rate and sending at it is how the account gets
    /// throttled. Fourteen per second is comfortably under a 14/sec quota once
    /// you account for a second replica.
    /// </remarks>
    public TimeSpan BetweenSends { get; set; } = TimeSpan.FromMilliseconds(140);

    /// <summary>How often stranded rows are recovered.</summary>
    public TimeSpan SweepEvery { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Claims messages and sends them, forever.
/// </summary>
/// <remarks>
/// The whole worker. It holds no state between iterations on purpose: every
/// answer about what to do next comes from the database, so killing this
/// process at any moment loses nothing except the message currently in flight,
/// which the sweeper recovers.
/// </remarks>
public sealed class SendLoop(
    MessageQueue queue,
    IEmailProvider provider,
    IOptions<SendLoopOptions> options,
    TimeProvider clock,
    ILogger<SendLoop> log) : BackgroundService
{
    private readonly SendLoopOptions _options = options.Value;

    /// <summary>Identifies which worker holds a claim, for the sweeper and for support.</summary>
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Send loop started as {WorkerId}.", _workerId);
        var nextSweep = clock.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (clock.GetUtcNow() >= nextSweep)
                {
                    var recovered = await queue.SweepExpiredLocksAsync(stoppingToken);
                    if (recovered > 0)
                    {
                        log.LogWarning(
                            "Recovered {Count} messages from a worker that stopped mid-send.",
                            recovered);
                    }

                    nextSweep = clock.GetUtcNow().Add(_options.SweepEvery);
                }

                var claimed = await queue.ClaimAsync(
                    _workerId, _options.BatchSize, stoppingToken);

                if (claimed.Count == 0)
                {
                    await Task.Delay(_options.IdleDelay, clock, stoppingToken);
                    continue;
                }

                foreach (var message in claimed)
                {
                    // Checked rather than assumed: cancellation arriving
                    // mid-batch should stop us here, leaving the rest claimed
                    // for the sweeper, instead of sending another twenty-four.
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await SendOneAsync(message, stoppingToken);
                    await Task.Delay(_options.BetweenSends, clock, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let one bad iteration end the loop. A worker that
                // exits on an unexpected error stops all mail — including
                // login links — until somebody notices, which is a far worse
                // outcome than retrying in five seconds.
                log.LogError(ex, "The send loop hit an error and will retry.");
                await Task.Delay(_options.IdleDelay, clock, stoppingToken);
            }
        }

        log.LogInformation("Send loop stopped.");
    }

    private async Task SendOneAsync(ClaimedMessage message, CancellationToken ct)
    {
        // Everything below is logged under the id of the request that queued
        // this, which happened in another process some minutes ago. That is
        // the whole point of carrying it on the row.
        using var _ = LogContext.PushProperty(
            Telemetry.CorrelationIdProperty, message.CorrelationId);

        var outcome = await provider.SendAsync(message, ct);

        if (outcome.Accepted)
        {
            // 'sent' means the provider accepted it, not that it arrived.
            // 'delivered' comes later by webhook, and conflating the two means
            // believing a blast worked when half of it bounced.
            await queue.MarkSentAsync(message.Id, outcome.ProviderMessageId!, ct);
            log.LogInformation(
                "Message accepted by the provider. {event}", Events.MessageSent);
            return;
        }

        var failure = DeliveryFailure.Classify(
            outcome.StatusCode, outcome.ProviderCode, outcome.Error);

        await queue.RecordFailureAsync(message.Id, failure, outcome.Error ?? "unknown", ct);

        // A permanent failure that is the address's fault stops us mailing it
        // again on any lane. Retrying a hard bounce is how a sending domain
        // gets blocked, and our own render errors are excluded because the
        // recipient did nothing wrong.
        if (failure == FailureClass.PermanentAndSuppress)
        {
            await queue.SuppressAsync(message.ToEmail, "hard_bounce", ct);
            log.LogWarning(
                "Suppressed an address after a permanent failure. {event}",
                Events.AddressSuppressed);
        }
    }
}
