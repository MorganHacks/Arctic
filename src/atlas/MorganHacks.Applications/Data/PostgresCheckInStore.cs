using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using Npgsql;

namespace MorganHacks.Applications.Data;

/// <summary>
/// The check-in desk's read of <c>applications.*</c>.
/// </summary>
/// <remarks>
/// One statement, on the unique index the code was minted against. It runs
/// once per person arriving, in front of a queue, on whatever the venue's
/// network is doing that morning, so it selects five columns and joins
/// nothing.
/// </remarks>
public sealed class PostgresCheckInStore(NpgsqlDataSource dataSource) : ICheckInStore
{
    /// <inheritdoc />
    public async Task<CheckInSubject?> FindByCodeAsync(
        string code, CancellationToken ct = default)
    {
        // No event in the WHERE clause. The code is unique across the table
        // because a volunteer holds nothing else, and narrowing on an event
        // here would mean the endpoint guessing which year somebody meant.
        const string sql = """
            SELECT id, status, first_name, last_name, checked_in_at
              FROM applications.applications
             WHERE check_in_code = @code
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("code", code);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new CheckInSubject(
            reader.GetGuid(0),
            ApplicationStatuses.Parse(reader.GetString(1)),
            await reader.IsDBNullAsync(2, ct) ? null : reader.GetString(2),
            await reader.IsDBNullAsync(3, ct) ? null : reader.GetString(3),
            await reader.IsDBNullAsync(4, ct)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(4));
    }
}
