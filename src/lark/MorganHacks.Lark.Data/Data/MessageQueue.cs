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
    /// <summary>
    /// Queues one transactional message, rendered now and stored.
    /// </summary>
    /// <remarks>
    /// A campaign per send, which looks heavier than it is. A campaign is the
    /// intent, and each login link genuinely is its own intent — while the
    /// unique index on (campaign_id, person_id) is what stops a broadcast
    /// going out twice. Sharing one campaign across every login link would
    /// mean either dropping that index or refusing somebody a second sign-in
    /// link, and both are worse than an extra row.
    /// </remarks>
    public async Task<Guid> EnqueueTransactionalAsync(
        EmailTemplate template,
        string toEmail,
        Guid? personId,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default)
    {
        var rendered = TemplateRenderer.Render(template, values);

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        Guid campaignId;
        const string campaign = """
            INSERT INTO notify.campaigns (template_id, name, status, recipient_count, queued_at)
            VALUES (@templateId, @name, 'queued', 1, now())
            RETURNING id
            """;
        await using (var cmd = new NpgsqlCommand(campaign, connection, transaction))
        {
            cmd.Parameters.AddWithValue("templateId", template.Id);
            cmd.Parameters.AddWithValue("name", template.Key);
            campaignId = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
        }

        Guid messageId;
        const string message = """
            INSERT INTO notify.messages
                (campaign_id, person_id, to_email, priority,
                 rendered_subject, rendered_body_html, rendered_body_text)
            VALUES (@campaignId, @personId, @toEmail, @priority, @subject, @html, @text)
            RETURNING id
            """;
        await using (var cmd = new NpgsqlCommand(message, connection, transaction))
        {
            cmd.Parameters.AddWithValue("campaignId", campaignId);
            cmd.Parameters.AddWithValue("personId", (object?)personId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("toEmail", toEmail);
            cmd.Parameters.AddWithValue("priority", template.Priority);
            cmd.Parameters.AddWithValue("subject", rendered.Subject);
            cmd.Parameters.AddWithValue("html", rendered.BodyHtml);
            cmd.Parameters.AddWithValue("text", rendered.BodyText);
            messageId = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
        }

        await transaction.CommitAsync(ct);
        return messageId;
    }

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
    /// The suppression check is part of the claim rather than a call the
    /// sender is trusted to remember. A bounced address that stays claimable
    /// is one forgotten <c>if</c> away from being mailed anyway, and that is
    /// how a sending domain gets blocked. Lane rules match
    /// <see cref="IsSuppressedAsync"/>: a bounce or complaint blocks both,
    /// an unsubscribe blocks broadcast only.
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
              FROM notify.campaigns c
              JOIN notify.templates t ON t.id = c.template_id
             WHERE c.id = m.campaign_id
               AND m.id IN (
                   SELECT q.id FROM notify.messages q
                    WHERE q.status = 'pending'
                      AND (q.next_attempt_at IS NULL OR q.next_attempt_at <= now())
                      AND NOT EXISTS (
                            SELECT 1 FROM notify.suppressions s
                             WHERE s.email = q.to_email
                               AND (s.reason <> 'unsubscribed' OR q.priority > 0))
                    ORDER BY q.priority, q.created_at
                    LIMIT @batch
                    FOR UPDATE OF q SKIP LOCKED
             )
            RETURNING m.id, m.campaign_id, m.to_email, m.priority, m.attempts,
                      m.rendered_subject, m.rendered_body_html, m.rendered_body_text,
                      t.from_local || '@' || t.from_domain, t.reply_to
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
                reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8),
                await reader.IsDBNullAsync(9, ct) ? null : reader.GetString(9)));
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
        // Attempts is incremented here rather than at claim time, so a message
        // is only charged for an attempt that actually reached the provider.
        // The sweeper charges one too, for the case where the worker died
        // before it could get here — otherwise a message that crashes its
        // worker is never charged at all and retries forever.
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
        // Recovering the row has to cost an attempt.
        //
        // Returning it to 'pending' untouched is the obvious version and it
        // builds an infinite loop: a message that crashes the worker is swept
        // back, claimed, crashes it again, forever. MaxAttempts cannot stop it
        // because that count is only raised by RecordFailureAsync, which is
        // exactly the code a dying process never reaches. Charging the attempt
        // here is what makes a poison message eventually give up instead of
        // taking the queue down with it.
        const string sql = """
            UPDATE notify.messages
               SET attempts = attempts + 1,
                   status = CASE
                       WHEN attempts + 1 >= @maxAttempts THEN 'failed_perm'
                       ELSE 'pending'
                   END,
                   locked_by = NULL,
                   locked_until = NULL,
                   last_error = COALESCE(last_error, 'worker stopped mid-send')
             WHERE status = 'sending' AND locked_until < now()
            """;
        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("maxAttempts", (short)RetrySchedule.MaxAttempts);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Records a suppression and stops anything already queued to that address.
    /// </summary>
    /// <remarks>
    /// The claim query would skip those rows anyway, but skipping leaves them
    /// pending forever and pending is the queue's way of saying "still owed".
    /// Marking them says what actually happened, and keeps the backlog
    /// readable when someone asks why a blast shows fewer sends than
    /// recipients.
    /// </remarks>
    public async Task SuppressAsync(string email, string reason, CancellationToken ct = default)
    {
        const string record = """
            INSERT INTO notify.suppressions (email, reason) VALUES (@email, @reason)
            ON CONFLICT (email) DO NOTHING
            """;
        await using (var cmd = dataSource.CreateCommand(record))
        {
            cmd.Parameters.AddWithValue("email", email);
            cmd.Parameters.AddWithValue("reason", reason);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Same lane rule as the claim query and IsSuppressedAsync: an
        // unsubscribe stops the announcements sitting in the queue and leaves
        // their login link alone.
        const string cancel = """
            UPDATE notify.messages
               SET status = 'suppressed', locked_by = NULL, locked_until = NULL
             WHERE status = 'pending'
               AND to_email = @email
               AND (@reason <> 'unsubscribed' OR priority > 0)
            """;
        await using (var cmd = dataSource.CreateCommand(cancel))
        {
            cmd.Parameters.AddWithValue("email", email);
            cmd.Parameters.AddWithValue("reason", reason);
            await cmd.ExecuteNonQueryAsync(ct);
        }
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
