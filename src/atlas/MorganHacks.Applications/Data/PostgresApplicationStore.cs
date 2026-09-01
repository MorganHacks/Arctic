using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using Npgsql;

namespace MorganHacks.Applications.Data;

/// <summary>
/// The <c>applications.*</c> schema. Nothing outside this module touches it.
/// </summary>
public sealed class PostgresApplicationStore(NpgsqlDataSource dataSource) : IApplicationStore
{
    /// <summary>
    /// Creates the row the moment someone starts the form, rather than when
    /// they submit.
    /// </summary>
    /// <remarks>
    /// The first history row is written here, in the same transaction. An
    /// application whose trail begins at its second status is one whose trail
    /// cannot be trusted.
    /// </remarks>
    public async Task<Guid> StartAsync(
        Guid eventId, string email, Guid? personId = null, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        Guid id;
        const string insert = """
            INSERT INTO applications.applications (event_id, person_id, email)
            VALUES (@eventId, @personId, @email)
            RETURNING id
            """;
        await using (var cmd = new NpgsqlCommand(insert, connection, transaction))
        {
            cmd.Parameters.AddWithValue("eventId", eventId);
            cmd.Parameters.AddWithValue("personId", (object?)personId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("email", email);
            id = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
        }

        await WriteHistoryAsync(
            connection, transaction, id, null, ApplicationStatus.Incomplete,
            null, null, null, ct);

        await transaction.CommitAsync(ct);
        return id;
    }

    public async Task<ApplicationStatus?> StatusOfAsync(
        Guid applicationId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT status FROM applications.applications WHERE id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return await cmd.ExecuteScalarAsync(ct) is string wire
            ? ApplicationStatuses.Parse(wire)
            : null;
    }

    /// <inheritdoc />
    public async Task<StatusChange> TransitionAsync(
        Guid applicationId,
        ApplicationStatus next,
        Guid? actorId = null,
        string? reason = null,
        Guid? batchId = null,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // FOR UPDATE, so the status is read and replaced under one row lock.
        //
        // Two reviewers reaching the same application at the same moment is
        // ordinary, not exotic — that is what a shared queue is. Without the
        // lock both read 'under_review', both find their change legal, and the
        // application ends up accepted with a history row saying it was
        // rejected. With it the second waits, re-reads 'accepted', and its
        // transition is judged against what is actually true.
        ApplicationStatus current;
        const string read = """
            SELECT status FROM applications.applications WHERE id = @id FOR UPDATE
            """;
        await using (var cmd = new NpgsqlCommand(read, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", applicationId);
            if (await cmd.ExecuteScalarAsync(ct) is not string wire)
            {
                throw new InvalidOperationException($"No application {applicationId}.");
            }

            current = ApplicationStatuses.Parse(wire);
        }

        StatusTransition.Validate(current, next);

        // The lifecycle timestamps move with the status rather than being left
        // to each caller. Every one of them is something somebody would
        // eventually forget to set, and a decided_at that disagrees with the
        // status is worse than not having the column.
        const string update = """
            UPDATE applications.applications
               SET status = @next,
                   updated_at = now(),
                   submitted_at = CASE WHEN @next = 'submitted'
                        THEN now() ELSE submitted_at END,
                   decided_at = CASE WHEN @next IN ('accepted', 'rejected', 'waitlisted')
                        THEN now() ELSE decided_at END,
                   decided_by = CASE WHEN @next IN ('accepted', 'rejected', 'waitlisted')
                        THEN @actorId ELSE decided_by END,
                   confirmed_at = CASE WHEN @next = 'confirmed'
                        THEN now() ELSE confirmed_at END,
                   declined_at = CASE WHEN @next = 'declined'
                        THEN now() ELSE declined_at END,
                   checked_in_at = CASE WHEN @next = 'checked_in'
                        THEN now() ELSE checked_in_at END,
                   checked_in_by = CASE WHEN @next = 'checked_in'
                        THEN @actorId ELSE checked_in_by END
             WHERE id = @id
            """;
        await using (var cmd = new NpgsqlCommand(update, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", applicationId);
            cmd.Parameters.AddWithValue("next", next.ToWire());
            cmd.Parameters.AddWithValue("actorId", (object?)actorId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var at = await WriteHistoryAsync(
            connection, transaction, applicationId, current, next, actorId, reason, batchId, ct);

        // One commit for both writes. If the status and its history row can
        // drift apart they eventually will, and an audit trail that is
        // sometimes wrong is one nobody can rely on when it matters.
        await transaction.CommitAsync(ct);

        return new StatusChange(applicationId, current, next, actorId, reason, batchId, at);
    }

    public async Task<IReadOnlyList<StatusChange>> HistoryOfAsync(
        Guid applicationId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT from_status, to_status, actor_id, reason, batch_id, created_at
              FROM applications.status_history
             WHERE application_id = @id
             ORDER BY created_at, id
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", applicationId);

        var history = new List<StatusChange>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            history.Add(new StatusChange(
                applicationId,
                await reader.IsDBNullAsync(0, ct)
                    ? null
                    : ApplicationStatuses.Parse(reader.GetString(0)),
                ApplicationStatuses.Parse(reader.GetString(1)),
                await reader.IsDBNullAsync(2, ct) ? null : reader.GetGuid(2),
                await reader.IsDBNullAsync(3, ct) ? null : reader.GetString(3),
                await reader.IsDBNullAsync(4, ct) ? null : reader.GetGuid(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return history;
    }

    private static async Task<DateTimeOffset> WriteHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid applicationId,
        ApplicationStatus? from,
        ApplicationStatus to,
        Guid? actorId,
        string? reason,
        Guid? batchId,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO applications.status_history
                (application_id, from_status, to_status, actor_id, reason, batch_id)
            VALUES (@applicationId, @from, @to, @actorId, @reason, @batchId)
            RETURNING created_at
            """;

        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("applicationId", applicationId);
        cmd.Parameters.AddWithValue("from", (object?)from?.ToWire() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("to", to.ToWire());
        cmd.Parameters.AddWithValue("actorId", (object?)actorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("batchId", (object?)batchId ?? DBNull.Value);

        // Read through the reader rather than casting the scalar: Npgsql hands
        // back a DateTime for timestamptz, and the conversion belongs here
        // rather than in an unchecked cast that only fails at runtime.
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return reader.GetFieldValue<DateTimeOffset>(0);
    }
}
