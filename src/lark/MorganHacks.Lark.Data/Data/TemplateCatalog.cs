using MorganHacks.Lark.Data.Domain;
using Npgsql;

namespace MorganHacks.Lark.Data.Data;

/// <summary>
/// The editing side of <c>notify.templates</c>.
/// </summary>
/// <remarks>
/// Separate from <see cref="TemplateStore"/> for the reason
/// <see cref="CampaignStore"/> is separate from <see cref="MessageQueue"/>:
/// they touch one table and have opposite risks. That one is on the path of
/// somebody signing in and does a single indexed read; this one is behind an
/// admin screen and rewrites the rows every queued message points at.
/// <para>
/// <b>Editing copies rather than overwrites.</b> A save retires the live row
/// and writes a new one a version higher, so every <c>campaigns.template_id</c>
/// keeps pointing at the exact wording that was approved and sent. 0017 has the
/// full argument; the consequence for callers is that
/// <see cref="ReviseAsync"/> returns a row with a different <c>Id</c> from the
/// one that went in, and that this is the point rather than a surprise.
/// </para>
/// </remarks>
public sealed class TemplateCatalog(NpgsqlDataSource dataSource)
{
    /// <summary>Postgres' code for a unique violation.</summary>
    private const string Duplicate = "23505";

    private const string Columns = """
        id, key, kind, subject, body_format, body_markdown, body_html, body_text,
        from_local, from_domain, reply_to, version, created_at, created_by
        """;

    /// <summary>
    /// Every live template, by key.
    /// </summary>
    /// <remarks>
    /// Retired versions are left out. A key with four rows behind it is one
    /// template as far as anybody choosing one is concerned, and a list that
    /// showed every version would grow by one row every time somebody fixed a
    /// typo.
    /// </remarks>
    public async Task<IReadOnlyList<TemplateVersion>> ListAsync(CancellationToken ct = default)
    {
        var sql = $"""
            SELECT {Columns}
              FROM notify.templates
             WHERE superseded_at IS NULL
             ORDER BY key
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var templates = new List<TemplateVersion>();
        while (await reader.ReadAsync(ct))
        {
            templates.Add(Read(reader));
        }

        return templates;
    }

    /// <summary>The live version of one template.</summary>
    public async Task<TemplateVersion?> FindAsync(string key, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT {Columns}
              FROM notify.templates
             WHERE key = @key AND superseded_at IS NULL
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("key", key);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>Writes a template that did not exist, at version 1.</summary>
    public async Task<TemplateWrite> CreateAsync(
        TemplateDraft draft, Guid author, CancellationToken ct = default)
    {
        var sql = $"""
            INSERT INTO notify.templates
                (key, kind, subject, body_format, body_markdown, body_html,
                 body_text, from_local, from_domain, reply_to, version, created_by)
            VALUES (@key, @kind, @subject, @format, @source, @html,
                    @text, @fromLocal, @fromDomain, @replyTo, 1, @author)
            RETURNING {Columns}
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        Bind(cmd, draft, author);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new TemplateWrite(TemplateWriteResult.Written, Read(reader));
        }
        catch (PostgresException e) when (e.SqlState == Duplicate)
        {
            // The partial unique index on live keys, not a check in this
            // method. Two people creating the same key at once is a race no
            // read-then-write can win.
            return new TemplateWrite(TemplateWriteResult.KeyTaken);
        }
    }

    /// <summary>
    /// Retires the live version and writes the next one.
    /// </summary>
    /// <remarks>
    /// One transaction, and the retirement happens first. The conditional
    /// <c>UPDATE</c> is what makes two simultaneous saves safe: Postgres
    /// re-checks <c>superseded_at IS NULL</c> after waiting on the row lock, so
    /// the second writer retires nothing and is told to reload rather than
    /// forking the template into two live rows.
    /// <para>
    /// The kind is compared against what the retirement returned rather than
    /// against a read taken earlier, so the comparison is inside the same
    /// transaction as the write. A mismatch rolls the retirement back, which is
    /// why this cannot leave a template with no live version.
    /// </para>
    /// </remarks>
    public async Task<TemplateWrite> ReviseAsync(
        string key, TemplateDraft draft, Guid author, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var previousId = Guid.Empty;
        var settled = string.Empty;
        var version = 0;
        var retired = false;

        await using (var retire = new NpgsqlCommand(
            """
            UPDATE notify.templates
               SET superseded_at = now()
             WHERE key = @key AND superseded_at IS NULL
            RETURNING id, kind, version
            """,
            connection,
            transaction))
        {
            retire.Parameters.AddWithValue("key", key);

            await using var reader = await retire.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                retired = true;
                previousId = reader.GetGuid(0);
                settled = reader.GetString(1);
                version = reader.GetInt32(2);
            }
        }

        if (!retired)
        {
            await transaction.RollbackAsync(ct);

            // Nothing to retire is two different things, and the caller says
            // two different sentences: a key that never existed, or one that
            // somebody else saved a moment ago.
            return new TemplateWrite(await ExistsAsync(key, ct)
                ? TemplateWriteResult.Superseded
                : TemplateWriteResult.NoSuchTemplate);
        }

        if (settled != draft.Kind)
        {
            // Refused here as well as by the trigger 0017 installs. The trigger
            // is what makes the rule true of the table; this is what makes the
            // refusal a sentence somebody can read instead of an exception.
            await transaction.RollbackAsync(ct);
            return new TemplateWrite(TemplateWriteResult.KindChanged, Kind: settled);
        }

        await using var insert = new NpgsqlCommand(
            $"""
            INSERT INTO notify.templates
                (key, kind, subject, body_format, body_markdown, body_html,
                 body_text, from_local, from_domain, reply_to, version, created_by)
            VALUES (@key, @kind, @subject, @format, @source, @html,
                    @text, @fromLocal, @fromDomain, @replyTo, @version, @author)
            RETURNING {Columns}
            """,
            connection,
            transaction);

        Bind(insert, draft with { Key = key }, author);
        insert.Parameters.AddWithValue("version", version + 1);

        TemplateVersion written;
        await using (var reader = await insert.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            written = Read(reader);
        }

        await transaction.CommitAsync(ct);

        // Asserted rather than assumed: the whole point of this method is that
        // the row a sent campaign points at is not the row that just changed.
        if (written.Id == previousId)
        {
            throw new InvalidOperationException(
                "A template revision reused the row it was meant to retire.");
        }

        return new TemplateWrite(TemplateWriteResult.Written, written);
    }

    /// <summary>Whether this key has ever existed, live or retired.</summary>
    private async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM notify.templates WHERE key = @key)");
        cmd.Parameters.AddWithValue("key", key);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static void Bind(NpgsqlCommand cmd, TemplateDraft draft, Guid author)
    {
        cmd.Parameters.AddWithValue("key", draft.Key);
        cmd.Parameters.AddWithValue("kind", draft.Kind);
        cmd.Parameters.AddWithValue("subject", draft.Subject);
        cmd.Parameters.AddWithValue("format", draft.Format);
        cmd.Parameters.AddWithValue("source", draft.Source);
        cmd.Parameters.AddWithValue("html", draft.Html);
        cmd.Parameters.AddWithValue("text", draft.Text);
        cmd.Parameters.AddWithValue("fromLocal", draft.FromLocal);
        cmd.Parameters.AddWithValue("fromDomain", draft.FromDomain);
        cmd.Parameters.AddWithValue("replyTo", (object?)draft.ReplyTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("author", author);
    }

    private static TemplateVersion Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8),
        reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.GetInt32(11),
        reader.GetFieldValue<DateTimeOffset>(12),
        reader.IsDBNull(13) ? null : reader.GetGuid(13));
}
