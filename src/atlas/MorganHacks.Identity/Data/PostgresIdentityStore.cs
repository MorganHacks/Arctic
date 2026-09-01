using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;
using Npgsql;

namespace MorganHacks.Identity.Data;

/// <summary>
/// The Identity module's own tables. Nothing outside this module reads them.
/// </summary>
public sealed class PostgresIdentityStore(NpgsqlDataSource dataSource) : IIdentityStore
{
    public async Task<Guid?> FindHackerIdByEmailAsync(string email, CancellationToken ct)
    {
        // `kind = 'hacker'` is load-bearing, not tidiness.
        //
        // Organizers sign in through Google so that access is tied to an
        // account we allowlisted and a subject id we bound on first login.
        // Without this clause, posting an organizer's address to
        // /auth/magic-link mails them a link that opens a session with every
        // permission they hold — the allowlist, the binding and Google itself
        // all skipped, on nothing stronger than reading an inbox.
        const string sql = """
            SELECT id FROM identity.people
            WHERE lower(email) = lower(@email)
              AND kind = 'hacker'
              AND revoked_at IS NULL
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("email", email);
        return await cmd.ExecuteScalarAsync(ct) as Guid?;
    }

    public async Task<IReadOnlyList<PersonSummary>> ListPeopleAsync(CancellationToken ct)
    {
        // Teams aggregated in the query rather than fetched per person. The
        // list is small either way, but a loop that issues one query per row is
        // the shape that stops being small quietly.
        const string sql = """
            SELECT p.id, p.kind, p.email, p.revoked_at IS NOT NULL,
                   coalesce(array_agg(t.slug ORDER BY t.slug)
                            FILTER (WHERE t.slug IS NOT NULL), '{}')
              FROM identity.people p
              LEFT JOIN identity.team_members m ON m.person_id = p.id
              LEFT JOIN identity.teams t ON t.id = m.team_id
             GROUP BY p.id, p.kind, p.email, p.revoked_at
             ORDER BY p.kind, lower(p.email)
            """;

        await using var cmd = dataSource.CreateCommand(sql);

        var people = new List<PersonSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            people.Add(new PersonSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                await reader.GetFieldValueAsync<string[]>(4, ct)));
        }

        return people;
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
        // The join to people is part of the condition, not a lookup. A link
        // issued before someone was revoked is still sitting in their inbox
        // afterwards, and revoking access has to mean that link is dead too.
        const string consume = """
            UPDATE identity.magic_link_tokens t
               SET consumed_at = @now
              FROM identity.people p
             WHERE p.id = t.person_id
               AND t.token_hash = @tokenHash
               AND t.consumed_at IS NULL
               AND t.expires_at > @now
               AND p.revoked_at IS NULL
            RETURNING t.person_id
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
            SELECT t.consumed_at, t.expires_at, p.revoked_at
              FROM identity.magic_link_tokens t
              JOIN identity.people p ON p.id = t.person_id
             WHERE t.token_hash = @tokenHash
            """;

        await using var classifyCmd = dataSource.CreateCommand(classify);
        classifyCmd.Parameters.AddWithValue("tokenHash", tokenHash);

        await using var reader = await classifyCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return TokenResult.Reject(TokenRejection.NotFound);
        }

        // Revoked is reported ahead of the token's own state: it is the
        // operative reason, and it stays true whatever the token looks like.
        if (!await reader.IsDBNullAsync(2, ct))
        {
            return TokenResult.Reject(TokenRejection.Revoked);
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
        // Revoking a person has to end their live sessions, not just stop
        // them starting new ones. Doing it here rather than expecting every
        // caller to also delete sessions means there is one place to get it
        // right instead of one place per admin action.
        const string sql = """
            SELECT s.person_id, s.expires_at, s.revoked_at, p.revoked_at
              FROM identity.sessions s
              JOIN identity.people p ON p.id = s.person_id
             WHERE s.token_hash = @tokenHash
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
        var revoked = !await reader.IsDBNullAsync(2, ct)
                      || !await reader.IsDBNullAsync(3, ct);

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

    public async Task<(IReadOnlyList<TeamMembership>, IReadOnlyList<PermissionGrant>,
        IReadOnlyList<TeamBaseline>)> GetPermissionContextAsync(
        Guid personId, CancellationToken ct)
    {
        var memberships = new List<TeamMembership>();
        var grants = new List<PermissionGrant>();
        var baselines = new List<TeamBaseline>();

        // Expiry is not filtered here. EffectivePermissions decides what is
        // still live, so there is exactly one place that rule exists and one
        // place to test it.
        const string membershipSql = """
            SELECT t.slug, m.expires_at
              FROM identity.team_members m
              JOIN identity.teams t ON t.id = m.team_id
             WHERE m.person_id = @personId
            """;
        await using (var cmd = dataSource.CreateCommand(membershipSql))
        {
            cmd.Parameters.AddWithValue("personId", personId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                memberships.Add(new TeamMembership(
                    reader.GetString(0),
                    await reader.IsDBNullAsync(1, ct)
                        ? null
                        : reader.GetFieldValue<DateTimeOffset>(1)));
            }
        }

        const string grantSql = """
            SELECT permission, expires_at FROM identity.grants WHERE person_id = @personId
            """;
        await using (var cmd = dataSource.CreateCommand(grantSql))
        {
            cmd.Parameters.AddWithValue("personId", personId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                // A permission the code no longer knows about is skipped
                // rather than granted. TryParse is the gate.
                if (Permission.TryParse(reader.GetString(0), out var permission))
                {
                    grants.Add(new PermissionGrant(
                        permission,
                        await reader.IsDBNullAsync(1, ct)
                            ? null
                            : reader.GetFieldValue<DateTimeOffset>(1)));
                }
            }
        }

        const string baselineSql = """
            SELECT t.slug, p.permission
              FROM identity.teams t
              JOIN identity.team_permissions p ON p.team_id = t.id
            """;
        var bySlug = new Dictionary<string, HashSet<Permission>>();
        await using (var cmd = dataSource.CreateCommand(baselineSql))
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!Permission.TryParse(reader.GetString(1), out var permission))
                {
                    continue;
                }

                var slug = reader.GetString(0);
                if (!bySlug.TryGetValue(slug, out var set))
                {
                    set = [];
                    bySlug[slug] = set;
                }

                set.Add(permission);
            }
        }

        baselines.AddRange(bySlug.Select(kv => new TeamBaseline(kv.Key, kv.Value)));
        return (memberships, grants, baselines);
    }

    public async Task<OrganizerResult> ResolveOrganizerAsync(
        GoogleIdentity identity, CancellationToken ct)
    {
        // 1. Known subject id wins, whatever the address now is. This is what
        //    keeps an organizer who changed their Google email signed in.
        const string bySubject = """
            SELECT id, revoked_at FROM identity.people
             WHERE google_sub = @sub AND kind = 'organizer'
            """;
        await using (var cmd = dataSource.CreateCommand(bySubject))
        {
            cmd.Parameters.AddWithValue("sub", identity.Subject);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var id = reader.GetGuid(0);
                return await reader.IsDBNullAsync(1, ct)
                    ? OrganizerResult.Accept(id)
                    : OrganizerResult.Reject(OrganizerRejection.Revoked);
            }
        }

        // 2. Otherwise the address must be on the allowlist, which is simply
        //    an organizer row existing for it.
        const string byEmail = """
            SELECT id, google_sub, revoked_at FROM identity.people
             WHERE lower(email) = lower(@email) AND kind = 'organizer'
            """;
        Guid personId;
        await using (var cmd = dataSource.CreateCommand(byEmail))
        {
            cmd.Parameters.AddWithValue("email", identity.Email);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return OrganizerResult.Reject(OrganizerRejection.NotAllowlisted);
            }

            personId = reader.GetGuid(0);

            if (!await reader.IsDBNullAsync(2, ct))
            {
                return OrganizerResult.Reject(OrganizerRejection.Revoked);
            }

            // Already bound to somebody else's Google account. Rebinding is a
            // deliberate admin action, never something a sign-in does.
            if (!await reader.IsDBNullAsync(1, ct))
            {
                return OrganizerResult.Reject(OrganizerRejection.BoundToAnotherAccount);
            }
        }

        // 3. First successful sign-in binds the subject id. Conditional on it
        //    still being null so two simultaneous first logins cannot both
        //    bind.
        const string bind = """
            UPDATE identity.people
               SET google_sub = @sub, updated_at = now()
             WHERE id = @id AND google_sub IS NULL
            RETURNING id
            """;
        await using (var cmd = dataSource.CreateCommand(bind))
        {
            cmd.Parameters.AddWithValue("sub", identity.Subject);
            cmd.Parameters.AddWithValue("id", personId);
            return await cmd.ExecuteScalarAsync(ct) is Guid bound
                ? OrganizerResult.Accept(bound)
                : OrganizerResult.Reject(OrganizerRejection.BoundToAnotherAccount);
        }
    }
}
