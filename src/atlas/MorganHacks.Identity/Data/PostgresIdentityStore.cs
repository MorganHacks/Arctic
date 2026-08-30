using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;
using Npgsql;

namespace MorganHacks.Identity.Data;

/// <summary>
/// The Identity module's own tables. Nothing outside this module reads them.
/// </summary>
public sealed class PostgresIdentityStore(NpgsqlDataSource dataSource) : IIdentityStore
{
    public async Task<Guid?> FindPersonIdByEmailAsync(string email, CancellationToken ct)
    {
        const string sql = """
            SELECT id FROM identity.people
            WHERE lower(email) = lower(@email) AND revoked_at IS NULL
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("email", email);
        return await cmd.ExecuteScalarAsync(ct) as Guid?;
    }

    public async Task InsertMagicLinkAsync(
        Guid personId, byte[] tokenHash, DateTimeOffset expiresAt, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO identity.magic_link_tokens (person_id, token_hash, expires_at)
            VALUES (@personId, @tokenHash, @expiresAt)
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("tokenHash", tokenHash);
        cmd.Parameters.AddWithValue("expiresAt", expiresAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<TokenResult> ConsumeMagicLinkAsync(
        byte[] tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        // One conditional UPDATE, not a SELECT then an UPDATE.
        //
        // Postgres takes a row lock for the duration of the update, so of two
        // clicks arriving at the same instant exactly one matches
        // `consumed_at IS NULL` and the other comes back empty. Splitting this
        // into a check and then a write would let both pass the check.
        //
        // This happens for real: mail clients and corporate link scanners
        // prefetch URLs, so the first "click" is often a machine.
        const string consume = """
            UPDATE identity.magic_link_tokens
               SET consumed_at = @now
             WHERE token_hash = @tokenHash
               AND consumed_at IS NULL
               AND expires_at > @now
            RETURNING person_id
            """;

        await using (var cmd = dataSource.CreateCommand(consume))
        {
            cmd.Parameters.AddWithValue("tokenHash", tokenHash);
            cmd.Parameters.AddWithValue("now", now);

            if (await cmd.ExecuteScalarAsync(ct) is Guid personId)
            {
                return TokenResult.Accept(personId);
            }
        }

        // Nothing was consumed. Work out why, for the caller's error message
        // only — the security decision was already made above.
        const string classify = """
            SELECT consumed_at, expires_at FROM identity.magic_link_tokens
            WHERE token_hash = @tokenHash
            """;

        await using var classifyCmd = dataSource.CreateCommand(classify);
        classifyCmd.Parameters.AddWithValue("tokenHash", tokenHash);

        await using var reader = await classifyCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return TokenResult.Reject(TokenRejection.NotFound);
        }

        var alreadyConsumed = !await reader.IsDBNullAsync(0, ct);
        return alreadyConsumed
            ? TokenResult.Reject(TokenRejection.AlreadyConsumed)
            : TokenResult.Reject(TokenRejection.Expired);
    }

    public async Task InsertSessionAsync(
        Guid personId, byte[] tokenHash, DateTimeOffset expiresAt,
        string? userAgent, string? ip, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO identity.sessions
                (person_id, token_hash, expires_at, user_agent, ip)
            VALUES (@personId, @tokenHash, @expiresAt, @userAgent, @ip::inet)
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("tokenHash", tokenHash);
        cmd.Parameters.AddWithValue("expiresAt", expiresAt);
        cmd.Parameters.AddWithValue("userAgent", (object?)userAgent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ip", (object?)ip ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<TokenResult> ValidateSessionAsync(
        byte[] tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            SELECT person_id, expires_at, revoked_at
              FROM identity.sessions
             WHERE token_hash = @tokenHash
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("tokenHash", tokenHash);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return TokenResult.Reject(TokenRejection.NotFound);
        }

        var personId = reader.GetGuid(0);
        var expiresAt = reader.GetFieldValue<DateTimeOffset>(1);
        var revoked = !await reader.IsDBNullAsync(2, ct);

        // Revocation is checked before expiry so that revoking a session that
        // was going to expire anyway still reports as revoked.
        if (revoked)
        {
            return TokenResult.Reject(TokenRejection.Revoked);
        }

        return expiresAt <= now
            ? TokenResult.Reject(TokenRejection.Expired)
            : TokenResult.Accept(personId);
    }

    public async Task RevokeSessionAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            UPDATE identity.sessions SET revoked_at = @now
            WHERE token_hash = @tokenHash AND revoked_at IS NULL
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("tokenHash", tokenHash);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RevokeAllSessionsForPersonAsync(
        Guid personId, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            UPDATE identity.sessions SET revoked_at = @now
            WHERE person_id = @personId AND revoked_at IS NULL
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
