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
    /// The first history row is written by a trigger, not here. An application
    /// whose trail begins at its second status is one whose trail cannot be
    /// trusted, and that has to hold for rows this method did not create.
    /// </remarks>
    public async Task<Guid> StartAsync(
        Guid eventId, string email, Guid? personId = null, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO applications.applications (event_id, person_id, email)
            VALUES (@eventId, @personId, @email)
            RETURNING id
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("personId", (object?)personId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("email", email);
        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
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

        // Who did this, and why, told to the transaction rather than passed to
        // an INSERT. The trigger writes the history row for every status
        // change including ones that never come through here, so this is how
        // it learns the things only the application knows.
        //
        // Transaction-local, so it cannot leak onto the next request that
        // borrows this pooled connection.
        const string context = """
            SELECT set_config('app.actor_id', @actorId, true),
                   set_config('app.reason', @reason, true),
                   set_config('app.batch_id', @batchId, true)
            """;
        await using (var cmd = new NpgsqlCommand(context, connection, transaction))
        {
            cmd.Parameters.AddWithValue("actorId", actorId?.ToString() ?? string.Empty);
            cmd.Parameters.AddWithValue("reason", reason ?? string.Empty);
            cmd.Parameters.AddWithValue("batchId", batchId?.ToString() ?? string.Empty);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Just the status. The lifecycle timestamps and the history row are
        // both the database's job now, which is what makes them hold for a
        // hand-written UPDATE during an incident as well as for this method.
        const string update = """
            UPDATE applications.applications
               SET status = @next
             WHERE id = @id
            RETURNING updated_at
            """;

        DateTimeOffset at;
        await using (var cmd = new NpgsqlCommand(update, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", applicationId);
            cmd.Parameters.AddWithValue("next", next.ToWire());
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);

            // now() is transaction start time, so this is the same instant the
            // trigger stamped on the history row.
            at = reader.GetFieldValue<DateTimeOffset>(0);
        }

        // The trigger's insert is part of this transaction, so the status and
        // its history row commit together or not at all. That was already true
        // when this method wrote both; it is now true for every writer.
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

}
