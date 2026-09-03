using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

namespace MorganHacks.Applications.Forms;

public sealed class PostgresFormStore(NpgsqlDataSource dataSource) : IFormStore
{
    /// <summary>
    /// How a form is written to and read from the column.
    /// </summary>
    /// <remarks>
    /// Enums as strings, not numbers. A stored 3 means nothing to anybody
    /// reading the row, and reordering the enum later would silently change
    /// what every existing form says.
    /// </remarks>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string Columns =
        "id, form_id, version, status, fields, created_at, published_at";

    /// <remarks>
    /// <c>eligible_statuses</c> rides along with <c>requires_sign_in</c>
    /// everywhere, because reading one without the other gives a form whose
    /// gate is on and whose audience is unknown — and the only safe reading of
    /// an unknown audience is nobody, which would close a form to the people
    /// it was built for.
    /// </remarks>
    private const string FormColumns =
        "id, event_id, code, name, kind, closes_at, requires_sign_in, eligible_statuses";

    public async Task<Form> CreateAsync(
        Guid eventId, string name, string kind, Guid? actorId, CancellationToken ct = default)
    {
        // Retried on collision rather than checked first. Seven characters from
        // a thirty-two character alphabet is a collision every few million
        // forms, so a loop that almost never runs beats a round trip that
        // always does — and the unique index is what makes it correct either
        // way.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var cmd = dataSource.CreateCommand(
                    $"INSERT INTO applications.forms (event_id, code, name, kind, created_by) "
                    + $"VALUES (@eventId, @code, @name, @kind, @actorId) RETURNING {FormColumns}");
                cmd.Parameters.AddWithValue("eventId", eventId);
                cmd.Parameters.AddWithValue("code", FormCode.Next());
                cmd.Parameters.AddWithValue("name", name);
                cmd.Parameters.AddWithValue("kind", kind);
                cmd.Parameters.AddWithValue("actorId", (object?)actorId ?? DBNull.Value);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                await reader.ReadAsync(ct);
                return ReadForm(reader);
            }
            catch (PostgresException e) when (e.SqlState == "23505" && attempt < 5
                                              && e.ConstraintName?.Contains("code") == true)
            {
                // Only a code collision is worth retrying. One application form
                // per event is a different unique index and a real error.
            }
        }
    }

    public async Task<Form?> ByCodeAsync(string code, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            $"SELECT {FormColumns} FROM applications.forms WHERE code = @code");
        cmd.Parameters.AddWithValue("code", code.Trim().ToLowerInvariant());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadForm(reader) : null;
    }

    public async Task<Form?> ByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            $"SELECT {FormColumns} FROM applications.forms WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadForm(reader) : null;
    }

    public async Task<IReadOnlyList<Form>> ForEventAsync(
        Guid eventId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            $"SELECT {FormColumns} FROM applications.forms "
            + "WHERE event_id = @eventId ORDER BY kind, name");
        cmd.Parameters.AddWithValue("eventId", eventId);

        var forms = new List<Form>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            forms.Add(ReadForm(reader));
        }

        return forms;
    }

    private static Form ReadForm(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
        reader.GetBoolean(6),
        reader.GetFieldValue<string[]>(7));

    public async Task<Form?> SaveAudienceAsync(
        Guid formId,
        bool requiresSignIn,
        IReadOnlyList<string> eligibleStatuses,
        CancellationToken ct = default)
    {
        // The statuses are cleared rather than kept when the gate goes off.
        // Left behind they would come back the day somebody turned it on
        // again, silently narrowing who may answer to whatever last year's
        // author chose — and the check constraint refuses to store them
        // anyway, so this is what turns that refusal into the obvious
        // behaviour rather than a 500.
        await using var cmd = dataSource.CreateCommand(
            $"UPDATE applications.forms "
            + "SET requires_sign_in = @gated, "
            + "    eligible_statuses = CASE WHEN @gated THEN @statuses ELSE '{}' END "
            + $"WHERE id = @id RETURNING {FormColumns}");

        cmd.Parameters.AddWithValue("id", formId);
        cmd.Parameters.AddWithValue("gated", requiresSignIn);
        cmd.Parameters.AddWithValue("statuses", eligibleStatuses.Distinct().ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadForm(reader) : null;
    }

    /// <summary>Takes a live form down, leaving everything it collected.</summary>
    /// <remarks>
    /// Retires the published version and puts nothing in its place, which is the
    /// same move publishing makes minus the second half. Nothing is deleted: the
    /// retired version still describes the questions its answers were given to,
    /// so the responses screen keeps working for a form nobody can fill in any
    /// more. Answers outlive the form that asked for them, which is the point.
    /// <para>
    /// Returns false when there was nothing published, so a second press is a
    /// no-op rather than an error.
    /// </para>
    /// </remarks>
    public async Task<bool> UnpublishAsync(Guid formId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "UPDATE applications.form_versions SET status = 'retired' "
            + "WHERE form_id = @formId AND status = 'published'");

        cmd.Parameters.AddWithValue("formId", formId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>When the form stops accepting answers on its own.</summary>
    /// <remarks>
    /// Null clears it, which is a form that stays open until somebody takes it
    /// down. The instant is stored as given; what timezone a person meant when
    /// they typed it is the console's problem, not this one's, and it is settled
    /// before the value arrives here.
    /// </remarks>
    public async Task<Form?> SaveScheduleAsync(
        Guid formId,
        DateTimeOffset? closesAt,
        CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "UPDATE applications.forms SET closes_at = @closesAt "
            + $"WHERE id = @id RETURNING {FormColumns}");

        cmd.Parameters.AddWithValue("id", formId);
        cmd.Parameters.AddWithValue("closesAt", (object?)closesAt ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadForm(reader) : null;
    }

    public async Task<FormVersion?> PublishedAsync(Guid formId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            $"SELECT {Columns} FROM applications.form_versions "
            + "WHERE form_id = @formId AND status = 'published'");
        cmd.Parameters.AddWithValue("formId", formId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task<FormVersion> DraftAsync(
        Guid formId, Guid? actorId, CancellationToken ct = default)
    {
        await using (var cmd = dataSource.CreateCommand(
            $"SELECT {Columns} FROM applications.form_versions "
            + "WHERE form_id = @formId AND status = 'draft'"))
        {
            cmd.Parameters.AddWithValue("formId", formId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return Read(reader);
            }
        }

        var published = await PublishedAsync(formId, ct);
        // A new form starts from the questions in StartingQuestions rather
        // than from nothing. They are only a starting point — every one of
        // them is editable from the moment the draft exists — but an author
        // who opens an empty page has to write ten ordinary questions before
        // reaching the one they came to add.
        var seed = published?.Fields ?? StartingQuestions.All;

        const string insert = """
            INSERT INTO applications.form_versions
                (form_id, event_id, version, status, fields, created_by)
            VALUES (
                @formId,
                (SELECT event_id FROM applications.forms WHERE id = @formId),
                coalesce((SELECT max(version) FROM applications.form_versions
                           WHERE form_id = @formId), 0) + 1,
                'draft', @fields::jsonb, @actorId)
            RETURNING id, form_id, version, status, fields, created_at, published_at
            """;

        await using var create = dataSource.CreateCommand(insert);
        create.Parameters.AddWithValue("formId", formId);
        create.Parameters.AddWithValue("fields", JsonSerializer.Serialize(seed, Json));
        create.Parameters.AddWithValue("actorId", (object?)actorId ?? DBNull.Value);

        await using var created = await create.ExecuteReaderAsync(ct);
        await created.ReadAsync(ct);
        return Read(created);
    }

    public async Task SaveDraftAsync(
        Guid formId, IReadOnlyList<FormField> fields, CancellationToken ct = default)
    {
        // Only the draft. A published form is frozen by a trigger as well, so
        // this narrowing is convenience rather than the guarantee.
        await using var cmd = dataSource.CreateCommand(
            "UPDATE applications.form_versions SET fields = @fields::jsonb "
            + "WHERE form_id = @formId AND status = 'draft'");
        cmd.Parameters.AddWithValue("formId", formId);
        cmd.Parameters.AddWithValue("fields", JsonSerializer.Serialize(fields, Json));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<FormVersion> PublishAsync(
        Guid formId, Guid? actorId, CancellationToken ct = default)
    {
        var draft = await DraftAsync(formId, actorId, ct);

        var problems = FormValidation.Check(draft.Fields);
        if (problems.Count > 0)
        {
            // Refused before anything is written. A half-published form is not
            // a state worth having recovery code for.
            throw new FormNotPublishableException(problems);
        }

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Retire first. A unique index allows one published form per event, so
        // doing this the other way round fails on the index rather than
        // swapping cleanly.
        await using (var cmd = new NpgsqlCommand(
            "UPDATE applications.form_versions SET status = 'retired' "
            + "WHERE form_id = @formId AND status = 'published'", connection, transaction))
        {
            cmd.Parameters.AddWithValue("formId", formId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        const string publish = """
            UPDATE applications.form_versions
               SET status = 'published', published_at = now(), published_by = @actorId
             WHERE id = @id
            RETURNING id, form_id, version, status, fields, created_at, published_at
            """;

        FormVersion published;
        await using (var cmd = new NpgsqlCommand(publish, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", draft.Id);
            cmd.Parameters.AddWithValue("actorId", (object?)actorId ?? DBNull.Value);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            published = Read(reader);
        }

        await transaction.CommitAsync(ct);
        return published;
    }

    public async Task<IReadOnlyList<FormVersion>> HistoryAsync(
        Guid formId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            $"SELECT {Columns} FROM applications.form_versions "
            + "WHERE form_id = @formId ORDER BY version DESC");
        cmd.Parameters.AddWithValue("formId", formId);

        var versions = new List<FormVersion>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            versions.Add(Read(reader));
        }

        return versions;
    }

    private static FormVersion Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetInt32(2),
        reader.GetString(3),
        JsonSerializer.Deserialize<List<FormField>>(reader.GetString(4), Json) ?? [],
        reader.GetFieldValue<DateTimeOffset>(5),
        reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6));
}
