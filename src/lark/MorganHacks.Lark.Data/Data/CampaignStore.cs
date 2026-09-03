using MorganHacks.Lark.Data.Domain;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Lark.Data.Data;

/// <summary>
/// Broadcasts, in Postgres.
/// </summary>
/// <remarks>
/// The other half of <see cref="MessageQueue"/>. That one queues a single
/// transactional message and is called on the path of somebody signing in;
/// this one queues several hundred at once and is called by an organizer who
/// has decided to mail everybody. Same tables, opposite risks — which is why
/// they are separate types rather than two methods on one.
/// <para>
/// It lives here rather than in atlas beside the endpoints that call it,
/// because <c>notify.*</c> is lark's schema and the rule in this codebase is
/// that a module owns its tables. Atlas resolves who the recipients are, since
/// that needs <c>applications.*</c> and lark has never heard of an applicant;
/// atlas then hands the list over and this writes it.
/// </para>
/// </remarks>
public sealed class CampaignStore(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Broadcast priority, written literally rather than derived.
    /// </summary>
    /// <remarks>
    /// <see cref="EmailTemplate.Priority"/> would give the same 10 for a
    /// template whose kind is 'broadcast', and that is one indirection away
    /// from a campaign pointed at a transactional template queueing several
    /// hundred messages at priority 0 — every one of them ahead of the sign-in
    /// links behind them in the same table. <see cref="QueueAsync"/> refuses
    /// that campaign outright; this constant is what makes the refusal
    /// unnecessary as well as correct.
    /// </remarks>
    private const short BroadcastPriority = 10;

    // ------------------------------------------------------------- reading ---

    /// <summary>Campaigns newest first.</summary>
    /// <remarks>
    /// Only the ones a person made. The transactional path in
    /// <see cref="MessageQueue.EnqueueTransactionalAsync"/> writes a campaign
    /// row per sign-in link, so by the end of registration week this table is
    /// mostly login links — and a broadcast console listing forty thousand of
    /// them is a console nobody can use. <c>created_by IS NOT NULL</c> is what
    /// separates them, because a magic link has no author and a broadcast
    /// always does.
    /// </remarks>
    public async Task<IReadOnlyList<Campaign>> ListAsync(
        int limit = 100, CancellationToken ct = default)
    {
        const string sql = $"""
            {Projection}
             WHERE c.created_by IS NOT NULL
             ORDER BY c.created_at DESC
             LIMIT @limit
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("limit", limit);

        var campaigns = new List<Campaign>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            campaigns.Add(Read(reader));
        }

        return campaigns;
    }

    public async Task<Campaign?> FindAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = $"""
            {Projection}
             WHERE c.id = @id AND c.created_by IS NOT NULL
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>
    /// How far a campaign's messages have actually got.
    /// </summary>
    /// <remarks>
    /// See <see cref="CampaignProgress"/> for why this is counted rather than
    /// read off the campaign row.
    /// </remarks>
    public async Task<CampaignProgress> ProgressAsync(Guid id, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT status, count(*) FROM notify.messages WHERE campaign_id = @id GROUP BY status");
        cmd.Parameters.AddWithValue("id", id);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            counts[reader.GetString(0)] = (int)reader.GetInt64(1);
        }

        return new CampaignProgress(counts);
    }

    /// <summary>
    /// A handful of the addresses a campaign was frozen against.
    /// </summary>
    /// <remarks>
    /// For the screen that answers "did this go where I thought". Capped and
    /// ordered, because the whole list is several hundred addresses and this
    /// is a sample, not an export — <c>applications.export</c> is the
    /// permission for taking a copy of who somebody is.
    /// </remarks>
    public async Task<IReadOnlyList<string>> SampleAsync(
        Guid id, int limit, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT to_email FROM notify.messages
             WHERE campaign_id = @id
             ORDER BY to_email
             LIMIT @limit
            """);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("limit", limit);

        var emails = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            emails.Add(reader.GetString(0));
        }

        return emails;
    }

    /// <summary>
    /// Which of these addresses may not be sent a broadcast, and why.
    /// </summary>
    /// <remarks>
    /// Every reason blocks, which is the difference between this lane and the
    /// other one. A hard bounce or a complaint blocks both lanes because a
    /// dead address is dead either way; an unsubscribe blocks this lane only,
    /// and this lane is the one it blocks. The mirror of that rule —
    /// an unsubscribe never standing between somebody and their sign-in link —
    /// lives in <see cref="MessageQueue.IsSuppressedAsync"/> and in the claim
    /// query, and is tested rather than assumed.
    /// <para>
    /// Checked here as well as in the claim query, which already refuses to
    /// hand a suppressed address to the provider. The duplication is the
    /// point: the claim check is what protects the sending domain, and this
    /// one is what makes the number on the confirmation screen the number of
    /// people who will actually receive the mail.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string>> SuppressedAmongAsync(
        IReadOnlyCollection<string> emails, CancellationToken ct = default)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (emails.Count == 0)
        {
            return found;
        }

        await using var cmd = dataSource.CreateCommand("""
            SELECT email, reason FROM notify.suppressions
             WHERE email = ANY(@emails::citext[])
            """);
        cmd.Parameters.Add(new NpgsqlParameter("emails", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = emails.ToArray(),
        });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            found[reader.GetString(0)] = reader.GetString(1);
        }

        return found;
    }

    /// <summary>
    /// Whether the circuit breaker has stopped broadcasts.
    /// </summary>
    /// <remarks>
    /// One row, so every replica agrees. Read at send rather than at draft:
    /// pausing exists for the moment SES starts refusing us, and the thing to
    /// stop then is new volume entering the queue, not somebody writing next
    /// week's announcement.
    /// </remarks>
    public async Task<(bool Paused, string? Reason)> BroadcastPauseAsync(
        CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT broadcast_paused, paused_reason FROM notify.state WHERE id");

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return (false, null);
        }

        return (reader.GetBoolean(0),
                await reader.IsDBNullAsync(1, ct) ? null : reader.GetString(1));
    }

    // ------------------------------------------------------------- writing ---

    /// <summary>
    /// Records the intent. Nothing is queued and nobody is mailed.
    /// </summary>
    /// <remarks>
    /// The segment is stored as it arrived rather than as the addresses it
    /// currently resolves to, and both halves of that matter. Storing the
    /// definition is what lets somebody read next month what the campaign was
    /// aimed at; resolving it at send rather than now is what stops a draft
    /// written on Monday mailing Monday's accepted list on Thursday.
    /// </remarks>
    public async Task<Campaign> CreateDraftAsync(
        Guid templateId,
        string name,
        string segment,
        Guid? eventId,
        Guid createdBy,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO notify.campaigns
                (template_id, name, segment, event_id, created_by, status)
            VALUES (@templateId, @name, @segment, @eventId, @createdBy, 'draft')
            RETURNING id
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("templateId", templateId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.Add(new NpgsqlParameter("segment", NpgsqlDbType.Jsonb) { Value = segment });
        cmd.Parameters.AddWithValue("eventId", (object?)eventId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdBy", createdBy);

        var id = (Guid)(await cmd.ExecuteScalarAsync(ct))!;

        // Read back rather than composed from the arguments, so the caller
        // gets the row as the database actually holds it — defaults, timestamp
        // and all — instead of an object that will diverge from it the first
        // time a default changes.
        return (await FindAsync(id, ct))!;
    }

    /// <summary>
    /// Freezes the recipient list and puts it in the queue. Sending it twice
    /// sends it once.
    /// </summary>
    /// <remarks>
    /// Two independent guards, because a duplicate blast is the one mistake in
    /// this system that cannot be undone — a few hundred people have the
    /// second copy the moment it lands, and there is no equivalent of a
    /// retraction.
    /// <para>
    /// The first is the conditional transition at the top:
    /// <c>WHERE status = 'draft'</c>, inside the transaction that writes the
    /// messages. A campaign leaves draft exactly once, and a second request —
    /// including one arriving at the same instant, which blocks on this row's
    /// lock until the first commits and then re-reads the status it set —
    /// matches nothing and writes nothing. Nothing here depends on the caller
    /// having checked the status first, because a check outside the
    /// transaction is a race dressed up as a guard.
    /// </para>
    /// <para>
    /// The second is the pair of unique indexes on the messages themselves:
    /// (campaign_id, person_id) from 0003 and (campaign_id, to_email) from
    /// 0015. <c>ON CONFLICT DO NOTHING</c> means that even if the transition
    /// guard were somehow bypassed, the second insert is a no-op rather than a
    /// second delivery. Two guards for one rule is not belt and braces here;
    /// the transition is what makes the API honest and the indexes are what
    /// make the outcome true regardless of which code wrote the rows.
    /// </para>
    /// <para>
    /// The rows themselves are the freeze. They are written before anything
    /// sends and they carry the rendered message, so restarting the worker
    /// mid-blast resumes rather than re-resolves — the alternative asks the
    /// segment again and mails whoever has been accepted since.
    /// </para>
    /// </remarks>
    public async Task<QueueOutcome> QueueAsync(
        Guid campaignId,
        Guid approvedBy,
        IReadOnlyList<BroadcastRecipient> recipients,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        const string claim = """
            UPDATE notify.campaigns
               SET status = 'queued', approved_by = @approvedBy, queued_at = now()
             WHERE id = @id AND status = 'draft'
            RETURNING id
            """;

        await using (var cmd = new NpgsqlCommand(claim, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", campaignId);
            cmd.Parameters.AddWithValue("approvedBy", approvedBy);

            if (await cmd.ExecuteScalarAsync(ct) is null)
            {
                await transaction.RollbackAsync(ct);
                return new QueueOutcome(
                    await ExistsAsync(campaignId, ct)
                        ? QueueResult.AlreadyLeftDraft
                        : QueueResult.NoSuchCampaign,
                    0, 0);
            }
        }

        // One statement for the whole list rather than a loop of inserts. A
        // segment can be several thousand people, and several thousand round
        // trips inside one transaction is a transaction long enough to matter
        // to everything else using the connection pool.
        const string insert = """
            INSERT INTO notify.messages
                (campaign_id, person_id, to_email, priority, status,
                 rendered_subject, rendered_body_html, rendered_body_text)
            SELECT @campaignId, r.person_id, r.email, @priority,
                   CASE WHEN r.suppressed THEN 'suppressed' ELSE 'pending' END,
                   r.subject, r.html, r.body
              FROM unnest(@personIds::uuid[], @emails::text[], @subjects::text[],
                          @htmls::text[], @bodies::text[], @suppressed::boolean[])
                   AS r(person_id, email, subject, html, body, suppressed)
            ON CONFLICT DO NOTHING
            """;

        await using (var cmd = new NpgsqlCommand(insert, connection, transaction))
        {
            cmd.Parameters.AddWithValue("campaignId", campaignId);
            cmd.Parameters.AddWithValue("priority", BroadcastPriority);
            Array(cmd, "personIds", NpgsqlDbType.Uuid,
                recipients.Select(r => (object?)r.PersonId ?? DBNull.Value).ToArray());
            Array(cmd, "emails", NpgsqlDbType.Text,
                recipients.Select(r => r.Email).ToArray());
            Array(cmd, "subjects", NpgsqlDbType.Text,
                recipients.Select(r => r.Subject).ToArray());
            Array(cmd, "htmls", NpgsqlDbType.Text,
                recipients.Select(r => r.BodyHtml).ToArray());
            Array(cmd, "bodies", NpgsqlDbType.Text,
                recipients.Select(r => r.BodyText).ToArray());
            Array(cmd, "suppressed", NpgsqlDbType.Boolean,
                recipients.Select(r => r.Suppressed).ToArray());

            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Counted from the rows that were written rather than from the list
        // that was passed in. The two agree today; if they ever stop agreeing,
        // the number on the screen should be the number of people who are
        // going to receive something.
        int queued;
        int suppressed;
        const string counts = """
            SELECT count(*) FILTER (WHERE status = 'pending'),
                   count(*) FILTER (WHERE status = 'suppressed')
              FROM notify.messages WHERE campaign_id = @id
            """;
        await using (var cmd = new NpgsqlCommand(counts, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", campaignId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            queued = (int)reader.GetInt64(0);
            suppressed = (int)reader.GetInt64(1);
        }

        const string record = """
            UPDATE notify.campaigns SET recipient_count = @count WHERE id = @id
            """;
        await using (var cmd = new NpgsqlCommand(record, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", campaignId);
            // The number who will be mailed, not the size of the segment. A
            // campaign that says 412 and sends 400 is a campaign somebody will
            // spend an afternoon reconciling; the twelve are still on the row
            // as 'suppressed' for whoever asks where they went.
            cmd.Parameters.AddWithValue("count", queued);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return new QueueOutcome(QueueResult.Queued, queued, suppressed);
    }

    /// <summary>
    /// Stops what has not gone. Says how much had.
    /// </summary>
    /// <remarks>
    /// Only <c>pending</c> rows are stopped. A row a worker has already
    /// claimed is somewhere between our process and SES, and pretending we
    /// called it back would be a worse answer than the true one — so
    /// <see cref="CancelOutcome.AlreadyGone"/> is returned rather than hidden,
    /// and a partly-sent campaign reads as exactly that.
    /// <para>
    /// Draft campaigns are cancellable too, which costs nothing and means
    /// abandoning one is the same gesture as stopping one. Anything already
    /// finished is left alone.
    /// </para>
    /// </remarks>
    public async Task<CancelOutcome> CancelAsync(Guid campaignId, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        const string stop = """
            UPDATE notify.campaigns
               SET status = 'cancelled', completed_at = now()
             WHERE id = @id AND status IN ('draft', 'queued', 'sending')
            RETURNING id
            """;

        await using (var cmd = new NpgsqlCommand(stop, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", campaignId);
            if (await cmd.ExecuteScalarAsync(ct) is null)
            {
                await transaction.RollbackAsync(ct);
                return new CancelOutcome(
                    await ExistsAsync(campaignId, ct)
                        ? CancelResult.NothingToStop
                        : CancelResult.NoSuchCampaign,
                    0, 0);
            }
        }

        int stopped;
        const string messages = """
            UPDATE notify.messages
               SET status = 'cancelled', locked_by = NULL, locked_until = NULL,
                   next_attempt_at = NULL
             WHERE campaign_id = @id AND status = 'pending'
            """;
        await using (var cmd = new NpgsqlCommand(messages, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", campaignId);
            stopped = await cmd.ExecuteNonQueryAsync(ct);
        }

        int gone;
        const string sent = """
            SELECT count(*) FROM notify.messages
             WHERE campaign_id = @id
               AND status IN ('sending','sent','delivered','bounced','complained',
                              'failed_temp','failed_perm')
            """;
        await using (var cmd = new NpgsqlCommand(sent, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", campaignId);
            gone = (int)(long)(await cmd.ExecuteScalarAsync(ct))!;
        }

        await transaction.CommitAsync(ct);
        return new CancelOutcome(CancelResult.Cancelled, stopped, gone);
    }

    // -------------------------------------------------------------- shared ---

    /// <summary>
    /// Every column the two readers need, joined to the template.
    /// </summary>
    /// <remarks>
    /// The template's key and kind ride along because every caller wants them
    /// and neither is on the campaign. The kind in particular is what a reader
    /// checks to know a campaign is a broadcast at all.
    /// </remarks>
    private const string Projection = """
        SELECT c.id, c.name, c.status, c.template_id, t.key, t.kind,
               c.event_id, c.segment, c.recipient_count,
               c.created_by, c.approved_by, c.queued_at, c.completed_at, c.created_at
          FROM notify.campaigns c
          JOIN notify.templates t ON t.id = c.template_id
        """;

    private static Campaign Read(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetGuid(3),
        r.GetString(4), r.GetString(5),
        r.IsDBNull(6) ? null : r.GetGuid(6),
        r.IsDBNull(7) ? null : r.GetString(7),
        r.GetInt32(8),
        r.IsDBNull(9) ? null : r.GetGuid(9),
        r.IsDBNull(10) ? null : r.GetGuid(10),
        r.IsDBNull(11) ? null : r.GetFieldValue<DateTimeOffset>(11),
        r.IsDBNull(12) ? null : r.GetFieldValue<DateTimeOffset>(12),
        r.GetFieldValue<DateTimeOffset>(13));

    private static void Array(
        NpgsqlCommand cmd, string name, NpgsqlDbType element, System.Array values) =>
        cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | element)
        {
            Value = values,
        });

    private async Task<bool> ExistsAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT 1 FROM notify.campaigns WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }
}

/// <summary>Whether there was anything to stop.</summary>
public enum CancelResult
{
    Cancelled,

    /// <summary>Already sent, already cancelled, or already failed.</summary>
    NothingToStop,

    NoSuchCampaign,
}

/// <summary>What cancelling caught, and what it did not.</summary>
public sealed record CancelOutcome(CancelResult Result, int Stopped, int AlreadyGone);
