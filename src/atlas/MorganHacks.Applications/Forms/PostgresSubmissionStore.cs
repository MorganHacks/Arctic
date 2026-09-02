using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Applications.Forms;

/// <summary>
/// Writes a completed application form into <c>applications.applications</c>.
/// </summary>
/// <remarks>
/// Every answer goes to one of two places, decided by the question and not by
/// the request: a real column when the form says so and the column is one we
/// recognise, the <c>responses</c> jsonb otherwise. That split is what keeps
/// the table useful — an answer that gets filtered, exported or read at
/// check-in earns a column, and the rest do not grow the table by one per
/// question per year.
/// </remarks>
public sealed class PostgresSubmissionStore(NpgsqlDataSource dataSource) : ISubmissionStore
{
    private static readonly JsonSerializerOptions Json = new();

    public async Task<Guid> RecordResumeAsync(
        Guid formId,
        string storageKey,
        string filename,
        int size,
        CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("""
            INSERT INTO applications.resume_uploads (form_id, storage_key, filename, size)
            VALUES (@form, @key, @filename, @size)
            RETURNING id
            """);

        cmd.Parameters.AddWithValue("form", formId);
        cmd.Parameters.AddWithValue("key", storageKey);
        cmd.Parameters.AddWithValue("filename", filename);
        cmd.Parameters.AddWithValue("size", size);

        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<Guid> SubmitApplicationAsync(
        Form form,
        FormVersion version,
        IReadOnlyDictionary<string, JsonElement> answers,
        CancellationToken ct = default)
    {
        if (!AnswerColumns.AsksForAnAddress(version.Fields))
        {
            throw new FormCannotCreateApplicantsException();
        }

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using var insert = new NpgsqlCommand { Connection = connection };
        insert.Transaction = transaction;

        var columns = new List<string>();
        var placeholders = new List<string>();

        void Set(string column, object? value)
        {
            var name = $"p{columns.Count}";
            columns.Add(column);
            placeholders.Add($"@{name}");
            insert.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        // Transaction time, the same instant the triggers stamp everything
        // else with. A timestamp computed in C# would sit a few milliseconds
        // away from the history row recording the same act, and "when did they
        // agree" is a question this column exists to answer exactly.
        void SetNow(string column)
        {
            columns.Add(column);
            placeholders.Add("now()");
        }

        Set("event_id", form.EventId);

        // Which questions they were shown. Together with the agreement
        // timestamps this is what makes an answer readable a year later.
        Set("form_version", version.Version);

        var responses = new Dictionary<string, object?>(StringComparer.Ordinal);
        Guid? resumeUpload = null;

        foreach (var field in version.Fields)
        {
            var answered = answers.TryGetValue(field.Key, out var value)
                           && SubmissionValidation.IsAnswered(field, value);

            // The resume is the one answer with columns of its own already, so
            // it never lands in responses.
            if (field.Type == FieldType.File)
            {
                if (answered)
                {
                    resumeUpload = SubmissionValidation.Upload(value);
                }

                continue;
            }

            // A question pointed at a column nobody recognises falls through
            // to responses rather than being dropped. The answer is kept
            // either way; only where it is kept changes, and an applicant
            // cannot do anything about a form naming a column wrong.
            if (field.Storage == AnswerStorage.Column
                && AnswerColumns.TryKindOf(field.Column, out var kind))
            {
                // The column name is written into the statement rather than
                // bound, because a column cannot be a parameter. It is safe
                // only because it came back from the allow-list above, and
                // that is the whole reason the allow-list exists.
                var column = field.Column!;

                if (kind == ColumnKind.Timestamp)
                {
                    // A tick becomes the moment it was ticked. Not ticked
                    // leaves the column null, which is the honest record —
                    // and the completeness constraint reads exactly that for
                    // the two agreements MLH requires.
                    if (answered)
                    {
                        SetNow(column);
                    }

                    continue;
                }

                if (!answered)
                {
                    // A boolean column is written false rather than skipped:
                    // "they did not opt in" is an answer, and leaving the
                    // default in place makes it look like the question was
                    // never asked.
                    if (kind == ColumnKind.Boolean)
                    {
                        Set(column, false);
                    }

                    continue;
                }

                Set(column, ColumnValue(kind, field, value));
                continue;
            }

            if (answered)
            {
                responses[field.Key] = ResponseValue(field, value);
            }
            else if (field.Type == FieldType.Consent)
            {
                // Same reasoning as the boolean column. An optional agreement
                // that was declined should read as declined, not as missing.
                responses[field.Key] = false;
            }
        }

        columns.Add("responses");
        placeholders.Add("@responses");
        insert.Parameters.Add(new NpgsqlParameter("responses", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(responses, Json),
        });

        if (resumeUpload is { } upload)
        {
            // Spent inside this transaction, so the two things that must agree
            // do. A submission that rolls back — the duplicate below is the
            // ordinary way — leaves the upload unspent and usable on the next
            // attempt, and an upload that is spent is one an application row
            // points at.
            var resume = await ClaimResumeAsync(connection, transaction, form.Id, upload, ct);

            // All three come from the row we wrote when we held the bytes,
            // including the size: the number is what was measured, not what
            // the request said it would be.
            Set("resume_key", resume.StorageKey);
            Set("resume_filename", resume.Filename);
            Set("resume_size", resume.Size);
            SetNow("resume_uploaded_at");
        }

        insert.CommandText =
            $"INSERT INTO applications.applications ({string.Join(", ", columns)}) "
            + $"VALUES ({string.Join(", ", placeholders)}) RETURNING id";

        Guid id;
        try
        {
            id = (Guid)(await insert.ExecuteScalarAsync(ct))!;
        }
        catch (PostgresException e) when (IsTheDedupeIndex(e))
        {
            throw new DuplicateApplicationException();
        }

        // Inserted as 'incomplete' and moved to 'submitted' in the same
        // transaction, rather than inserted as 'submitted' outright.
        //
        // The lifecycle triggers only stamp submitted_at on an UPDATE OF
        // status, so a row that arrives already submitted is one with no
        // submitted_at — and the review queue orders on that column. Going
        // through the transition also leaves the trail an application is
        // supposed to have: started, then submitted.
        await using (var submit = new NpgsqlCommand(
            "UPDATE applications.applications SET status = 'submitted' WHERE id = @id",
            connection, transaction))
        {
            submit.Parameters.AddWithValue("id", id);
            await submit.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return id;
    }

    /// <summary>
    /// Spends one upload, or refuses.
    /// </summary>
    /// <remarks>
    /// One statement, so the check and the spend cannot be separated. Reading
    /// the row first and updating it after leaves a gap two concurrent submits
    /// both get through, and what comes out the other side is two applications
    /// pointing at one person's resume.
    /// <para>
    /// The form id is part of the WHERE rather than something asserted
    /// afterwards. It is what stops an upload made against one form being spent
    /// on another — the two forms belong to different events, and the resume
    /// would end up filed under a cycle it was never sent to.
    /// </para>
    /// </remarks>
    private static async Task<(string StorageKey, string Filename, int Size)> ClaimResumeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid formId,
        Guid uploadId,
        CancellationToken ct)
    {
        await using var claim = new NpgsqlCommand("""
            UPDATE applications.resume_uploads
               SET claimed_at = now()
             WHERE id = @id AND form_id = @form AND claimed_at IS NULL
            RETURNING storage_key, filename, size
            """, connection, transaction);

        claim.Parameters.AddWithValue("id", uploadId);
        claim.Parameters.AddWithValue("form", formId);

        await using var reader = await claim.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new ResumeUploadNotClaimableException();
        }

        return (reader.GetString(0), reader.GetString(1), reader.GetInt32(2));
    }

    /// <summary>
    /// Whether this violation is one address applying twice.
    /// </summary>
    /// <remarks>
    /// Named, rather than treating every unique violation as a duplicate
    /// application. Any other index failing here is a bug, and telling
    /// somebody "you have already applied" would hide it behind a sentence
    /// that sounds reasonable.
    /// </remarks>
    private static bool IsTheDedupeIndex(PostgresException e) =>
        e.SqlState == PostgresErrorCodes.UniqueViolation
        && e.ConstraintName == "applications_event_email_key";

    private static object? ColumnValue(ColumnKind kind, FormField field, JsonElement value) =>
        kind switch
        {
            ColumnKind.Integer => SubmissionValidation.TryNumber(value, out var number)
                ? (int)decimal.Truncate(number)
                : null,

            // A ticked consent is true. Anything else routed at a boolean
            // column is read from its text, so a yes/no question can be
            // pointed at one without the form builder having to know how
            // Postgres spells it.
            ColumnKind.Boolean => field.Type == FieldType.Consent
                || IsAffirmative(SubmissionValidation.Text(value)),

            // Several choices in one text column become one line. The
            // separator is a display decision and this is the only place that
            // makes it, which is why the same answer also sits in responses
            // for anything that needs the parts back.
            ColumnKind.Text => field.Type == FieldType.Checkboxes
                ? string.Join(", ", Choices(value))
                : SubmissionValidation.Text(value),

            _ => null,
        };

    private static bool IsAffirmative(string text) =>
        text is "true" or "yes" or "1";

    private static object ResponseValue(FormField field, JsonElement value) => field.Type switch
    {
        FieldType.Checkboxes => Choices(value),
        FieldType.Consent => true,
        FieldType.Number => SubmissionValidation.TryNumber(value, out var number)
            ? number
            : SubmissionValidation.Text(value),
        _ => SubmissionValidation.Text(value),
    };

    private static string[] Choices(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? [.. value.EnumerateArray().Select(SubmissionValidation.Text)]
        : [];
}
