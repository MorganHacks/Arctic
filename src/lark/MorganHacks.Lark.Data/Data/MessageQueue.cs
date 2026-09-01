using MorganHacks.Lark.Data.Domain;
using Npgsql;

namespace MorganHacks.Lark.Data.Data;

/// <summary>
/// The send queue, in Postgres.
/// </summary>
/// <remarks>
/// There is no separate queue system because Postgres does this natively with
/// <c>FOR UPDATE SKIP LOCKED</c>. That is one fewer thing to run, pay for, and
/// have fall over at 2am during registration week.
/// </remarks>
public sealed class MessageQueue(NpgsqlDataSource dataSource)
{
    /// <summary>How long a claim is held before the sweeper may take it back.</summary>
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Takes up to <paramref name="batchSize"/> messages that are ready to send.
    /// </summary>
    /// <remarks>
    /// <c>SKIP LOCKED</c> is what makes this safe on several workers with no
    /// coordination between them: each takes a disjoint batch instead of
    /// blocking on the others.
    /// <para>
    /// Ordered by priority ascending, so a login link never queues behind two
    /// thousand announcements.
    /// </para>
    /// <para>
    /// This is at-least-once, not exactly-once. A worker that sends and then
    /// dies before recording it will send again. That is the right way round:
    /// a duplicate acceptance email is mildly awkward, a missing one costs an
    /// attendee.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ClaimedMessage>> ClaimAsync(
        string workerId, int batchSize, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE notify.messages m
               SET status = 'sending',
                   locked_by = @worker,
                   locked_until = now() + @lock
             WHERE m.id IN (
                   SELECT id FROM notify.messages
                    WHERE status = 'pending'
                      AND (next_attempt_at IS NULL OR next_attempt_at <= now())
                    ORDER BY priority, created_at
                    LIMIT @batch
                    FOR UPDATE SKIP LOCKED
             )
            RETURNING m.id, m.campaign_id, m.to_email, m.priority, m.attempts,
                      m.rendered_subject, m.rendered_body_html, m.rendered_body_text
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("worker", workerId);
        cmd.Parameters.AddWithValue("lock", LockDuration);
        cmd.Parameters.AddWithValue("batch", batchSize);

        var claimed = new List<ClaimedMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            claimed.Add(new ClaimedMessage(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetInt16(3), reader.GetInt16(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        }

        return claimed;
    }

    /// <summary>Records a successful hand-off to the provider.</summary>
    /// <remarks>
    /// <c>sent</c> means the provider accepted it. <c>delivered</c> means the
    /// recipient's server did, and only arrives later by webhook. Conflating
    /// them means believing a blast worked when half of it bounced.
    /// </remarks>
    public async Task MarkSentAsync(Guid id, string providerMessageId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE notify.messages
               SET status = 'sent', sent_at = now(), provider_message_id = @providerId,
                   locked_by = NULL, locked_until = NULL, last_error = NULL
             WHERE id = @id
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("providerId", providerMessageId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Applies the outcome of a failed attempt.</summary>
    public async Task RecordFailureAsync(
        Guid id, FailureClass failure, string error, CancellationToken ct = default)
    {
        // Attempts is incremented here rather than at claim time, so a worker
        // that dies mid-send does not burn one of the five.
        const string sql = """
            UPDATE notify.messages
               SET attempts = attempts + 1,
                   last_error = @error,
                   locked_by = NULL,
                   locked_until = NULL,
                   status = CASE
                       WHEN @permanent THEN 'failed_perm'
                       WHEN attempts + 1 >= @maxAttempts THEN 'failed_perm'
                       ELSE 'pending'
                   END,
                   next_attempt_at = CASE
                       WHEN @permanent OR attempts + 1 >= @maxAttempts THEN NULL
                       ELSE now() + @delay
                   END
             WHERE id = @id
            RETURNING to_email, status
            """;

        var permanent = failure is not FailureClass.Temporary;
        // Read attempts first so the delay matches the attempt being scheduled.
        var attempts = await CurrentAttemptsAsync(id, ct);
        var delay = RetrySchedule.DelayFor(attempts + 1) ?? TimeSpan.Zero;

        string? email = null;
        string? status = null;
        await using (var cmd = dataSource.CreateCommand(sql))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("error", error);
            cmd.Parameters.AddWithValue("permanent", permanent);
            cmd.Parameters.AddWithValue("maxAttempts", RetrySchedule.MaxAttempts);
            cmd.Parameters.AddWithValue("delay", delay);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                email = reader.GetString(0);
                status = reader.GetString(1);
            }
        }

        // Suppress only when the address is the problem. A render failure is
        // our bug, and suppressing for it would silently stop mailing someone
        // because of a mistake we made.
        if (failure is FailureClass.PermanentAndSuppress && email is not null)
        {
            await SuppressAsync(email, "hard_bounce", ct);
        }

        _ = status;
    }

    /// <summary>
    /// Returns rows whose worker died mid-claim to the pending pool.
    /// </summary>
    /// <remarks>
    /// Without this a crash between claiming and sending strands those rows in
    /// <c>sending</c> forever, and nobody notices until an applicant asks why
    /// they never got their decision.
    /// </remarks>
    public async Task<int> SweepExpiredLocksAsync(CancellationToken ct = default)
    {
        const string sql = """
            UPDATE notify.messages
               SET status = 'pending', locked_by = NULL, locked_until = NULL
             WHERE status = 'sending' AND locked_until < now()
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SuppressAsync(string email, string reason, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO notify.suppressions (email, reason) VALUES (@email, @reason)
            ON CONFLICT (email) DO NOTHING
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("reason", reason);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Whether an address may be sent to on a given lane.
    /// </summary>
    /// <remarks>
    /// A hard bounce or complaint blocks both lanes: a dead address is dead
    /// either way. An unsubscribe blocks broadcast only — someone who opted
    /// out of announcements must still receive their login link and their
    /// decision, which they asked for by acting.
    /// </remarks>
    public async Task<bool> IsSuppressedAsync(
        string email, bool transactional, CancellationToken ct = default)
    {
        const string sql = """
            SELECT reason FROM notify.suppressions WHERE email = @email
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("email", email);

        if (await cmd.ExecuteScalarAsync(ct) is not string reason)
        {
            return false;
        }

        return reason switch
        {
            "unsubscribed" => !transactional,
            _ => true,
        };
    }

    private async Task<short> CurrentAttemptsAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT attempts FROM notify.messages WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteScalarAsync(ct) as short? ?? 0;
    }
}
