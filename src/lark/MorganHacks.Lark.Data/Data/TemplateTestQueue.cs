using MorganHacks.Lark.Data.Domain;
using Npgsql;

namespace MorganHacks.Lark.Data.Data;

public sealed class TemplateTestQueue(NpgsqlDataSource dataSource)
{
    public async Task<Guid?> EnqueueAsync(TemplateDraft draft, string recipient, Guid author, Guid requestId, CancellationToken ct)
    {
        var template = new EmailTemplate(Guid.Empty, draft.Key, draft.Kind, $"[Test] {draft.Subject}",
            draft.Html, draft.Text, draft.FromLocal, draft.FromDomain, draft.ReplyTo, draft.FromName, draft.PreviewText);
        var rendered = TemplateRenderer.Render(template, new Dictionary<string, string> { ["email"] = recipient });
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@id::text, 0))", connection, transaction))
        {
            gate.Parameters.AddWithValue("id", requestId);
            await gate.ExecuteNonQueryAsync(ct);
        }
        await using (var existing = new NpgsqlCommand("SELECT c.created_by FROM notify.messages m JOIN notify.campaigns c ON c.id = m.campaign_id WHERE m.id = @id", connection, transaction))
        {
            existing.Parameters.AddWithValue("id", requestId);
            var owner = await existing.ExecuteScalarAsync(ct);
            if (owner is not null) return owner is Guid id && id == author ? requestId : null;
        }

        await using var command = new NpgsqlCommand("""
            WITH snapshot AS (
                INSERT INTO notify.templates
                    (key, kind, subject, body_format, body_markdown, body_html, body_text,
                     from_local, from_domain, from_name, reply_to, preview_text, click_tracking,
                     name, created_by, superseded_at)
                VALUES (@key, @kind, @subject, @format, @source, @html, @text,
                        @local, @domain, @sender, @reply, @preview, false,
                        @name, @author, now())
                RETURNING id
            ), campaign AS (
                INSERT INTO notify.campaigns (template_id, name, status, recipient_count, queued_at, created_by)
                SELECT id, @name, 'queued', 1, now(), @author FROM snapshot
                RETURNING id
            )
            INSERT INTO notify.messages (id, campaign_id, to_email, priority,
                rendered_subject, rendered_body_html, rendered_body_text)
            SELECT @id, id, @recipient, @priority, @renderedSubject, @renderedHtml, @renderedText FROM campaign
            """, connection, transaction);
        command.Parameters.AddWithValue("id", requestId);
        command.Parameters.AddWithValue("key", $"test_{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("kind", draft.Kind);
        command.Parameters.AddWithValue("subject", template.Subject);
        command.Parameters.AddWithValue("format", draft.Format);
        command.Parameters.AddWithValue("source", draft.Source);
        command.Parameters.AddWithValue("html", draft.Html);
        command.Parameters.AddWithValue("text", draft.Text);
        command.Parameters.AddWithValue("local", draft.FromLocal);
        command.Parameters.AddWithValue("domain", draft.FromDomain);
        command.Parameters.AddWithValue("sender", (object?)draft.FromName ?? DBNull.Value);
        command.Parameters.AddWithValue("reply", (object?)draft.ReplyTo ?? DBNull.Value);
        command.Parameters.AddWithValue("preview", (object?)draft.PreviewText ?? DBNull.Value);
        command.Parameters.AddWithValue("name", $"Test email: {draft.Name ?? draft.Subject}");
        command.Parameters.AddWithValue("author", author);
        command.Parameters.AddWithValue("recipient", recipient);
        command.Parameters.AddWithValue("priority", template.Priority);
        command.Parameters.AddWithValue("renderedSubject", rendered.Subject);
        command.Parameters.AddWithValue("renderedHtml", rendered.BodyHtml);
        command.Parameters.AddWithValue("renderedText", rendered.BodyText);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return requestId;
    }
}
