using Npgsql;

namespace MorganHacks.Audit;

/// <summary>
/// The <c>audit.entries</c> table, read-only.
/// </summary>
/// <remarks>
/// Every statement in this file is a SELECT, and there is nowhere in this
/// library that is not. Writing is the triggers' job; see
/// <see cref="IAuditTrail"/> for why.
/// </remarks>
public sealed class PostgresAuditTrail(NpgsqlDataSource dataSource) : IAuditTrail
{
    /// <summary>
    /// The most entries one read will return, whatever it was asked for.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than a validation error, because the caller is a query
    /// string somebody can edit and refusing <c>?limit=100000</c> teaches them
    /// nothing that clamping does not. The point is that no single request can
    /// pull the whole table into memory.
    /// </remarks>
    public const int MaxLimit = 500;

    public async Task<IReadOnlyList<AuditEntry>> ReadAsync(
        AuditQuery query, CancellationToken ct = default)
    {
        // The filters are folded into one WHERE with `@x IS NULL OR ...`
        // rather than assembled from string fragments. Two filters means four
        // combinations, and four hand-written queries is four places for the
        // ORDER BY to drift.
        //
        // Both partial indexes are still usable: the planner sees the constant
        // null through the parameter and drops the branch.
        const string sql = """
            SELECT id, occurred_at, action, actor_id, subject_id,
                   subject_team, target, expires_at, detail::text
              FROM audit.entries
             WHERE (@subject::uuid IS NULL OR subject_id = @subject)
               AND (@actor::uuid   IS NULL OR actor_id   = @actor)
               AND (@before::bigint IS NULL OR id < @before)
             ORDER BY id DESC
             LIMIT @limit
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("subject", (object?)query.Subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actor", (object?)query.Actor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("before", (object?)query.Before ?? DBNull.Value);
        cmd.Parameters.AddWithValue("limit", Math.Clamp(query.Limit, 1, MaxLimit));

        var entries = new List<AuditEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(new AuditEntry(
                reader.GetInt64(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetString(2),
                await reader.IsDBNullAsync(3, ct) ? null : reader.GetGuid(3),
                await reader.IsDBNullAsync(4, ct) ? null : reader.GetGuid(4),
                await reader.IsDBNullAsync(5, ct) ? null : reader.GetString(5),
                await reader.IsDBNullAsync(6, ct) ? null : reader.GetString(6),
                await reader.IsDBNullAsync(7, ct)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetString(8)));
        }

        return entries;
    }
}
