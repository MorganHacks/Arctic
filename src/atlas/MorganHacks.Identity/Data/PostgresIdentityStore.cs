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
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await RevokeSessionsAsync(conn, transaction: null, personId, now, ct);
    }

    /// <summary>
    /// The one definition of "end every session this person holds".
    /// </summary>
    /// <remarks>
    /// Takes a connection so that revoking a person can run it inside the same
    /// transaction as setting <c>revoked_at</c>. Two copies of this UPDATE —
    /// one for the standalone call, one for the transactional one — is how the
    /// two drift, and the copy that drifts is the one that leaves a revoked
    /// organizer with a working laptop.
    /// </remarks>
    private static async Task RevokeSessionsAsync(
        NpgsqlConnection conn, NpgsqlTransaction? transaction,
        Guid personId, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            UPDATE identity.sessions SET revoked_at = @now
            WHERE person_id = @personId AND revoked_at IS NULL
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, transaction);
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

    // ------------------------------------------------------- administration ---

    public async Task<PersonDetail?> FindPersonAsync(Guid personId, CancellationToken ct)
    {
        const string sql = """
            SELECT kind, email, revoked_at FROM identity.people WHERE id = @id
            """;

        string kind;
        string email;
        DateTimeOffset? revokedAt;

        await using (var cmd = dataSource.CreateCommand(sql))
        {
            cmd.Parameters.AddWithValue("id", personId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            kind = reader.GetString(0);
            email = reader.GetString(1);
            revokedAt = await reader.IsDBNullAsync(2, ct)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(2);
        }

        // Reuses the permission-context query rather than repeating its two
        // SELECTs here. It fetches every team's baseline as well, which this
        // caller throws away — sixty rows of a table nobody writes to, against
        // two copies of a membership query that would have to be kept in step.
        var (memberships, grants, _) = await GetPermissionContextAsync(personId, ct);

        return new PersonDetail(personId, kind, email, revokedAt, memberships, grants);
    }

    public async Task<IReadOnlyList<TeamSummary>> ListTeamsAsync(CancellationToken ct)
    {
        // LEFT JOIN so a team with no baseline still appears. One that grants
        // nothing is either a mistake somebody needs to see or a team being
        // set up, and neither is served by hiding it.
        const string sql = """
            SELECT t.slug, t.name, p.permission
              FROM identity.teams t
              LEFT JOIN identity.team_permissions p ON p.team_id = t.id
             ORDER BY t.name
            """;

        var order = new List<string>();
        var names = new Dictionary<string, string>();
        var permissions = new Dictionary<string, HashSet<Permission>>();

        await using var cmd = dataSource.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var slug = reader.GetString(0);
            if (!names.ContainsKey(slug))
            {
                order.Add(slug);
                names[slug] = reader.GetString(1);
                permissions[slug] = [];
            }

            // Same gate as everywhere else: a row naming a permission the code
            // has since dropped is ignored rather than displayed as real.
            if (!await reader.IsDBNullAsync(2, ct)
                && Permission.TryParse(reader.GetString(2), out var permission))
            {
                permissions[slug].Add(permission);
            }
        }

        return order
            .Select(slug => new TeamSummary(slug, names[slug], permissions[slug]))
            .ToList();
    }

    public async Task<AddOrganizerResult> AddOrganizerAsync(string email, CancellationToken ct)
    {
        // Insert first and ask questions afterwards. Checking whether the
        // address is taken and then inserting leaves a window two admins can
        // both pass through; the unique index on lower(email) is the only
        // thing that actually decides, so let it decide.
        const string insert = """
            INSERT INTO identity.people (kind, email) VALUES ('organizer', @email)
            ON CONFLICT DO NOTHING
            RETURNING id
            """;

        await using (var cmd = dataSource.CreateCommand(insert))
        {
            cmd.Parameters.AddWithValue("email", email.Trim());
            if (await cmd.ExecuteScalarAsync(ct) is Guid created)
            {
                return AddOrganizerResult.Accept(created);
            }
        }

        // Something already holds the address. Which kind of account it is
        // changes what the admin should do about it, so it is worth the second
        // query — this is a message for a person, not a security decision.
        const string existing = """
            SELECT kind FROM identity.people WHERE lower(email) = lower(@email)
            """;

        await using var lookup = dataSource.CreateCommand(existing);
        lookup.Parameters.AddWithValue("email", email.Trim());
        var kind = await lookup.ExecuteScalarAsync(ct) as string;

        // A null kind means the row was deleted between the two statements,
        // which nothing in this system does. Reporting the conflict we already
        // proved is better than inventing a third outcome for it.
        return kind == "hacker"
            ? AddOrganizerResult.Reject(AddOrganizerRejection.AddressIsAHackerAccount)
            : AddOrganizerResult.Reject(AddOrganizerRejection.AlreadyAnOrganizer);
    }

    public async Task<bool> AddToTeamAsync(
        Guid personId, string teamSlug, DateTimeOffset? expiresAt, CancellationToken ct)
    {
        // Selecting the two ids rather than passing them in means an unknown
        // person or an unknown team inserts nothing and returns nothing,
        // instead of raising a foreign-key error the caller has to decode.
        const string sql = """
            INSERT INTO identity.team_members (person_id, team_id, expires_at)
            SELECT p.id, t.id, @expiresAt
              FROM identity.people p, identity.teams t
             WHERE p.id = @personId AND t.slug = @slug
            ON CONFLICT (person_id, team_id) DO UPDATE SET expires_at = EXCLUDED.expires_at
            RETURNING person_id
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("slug", teamSlug);
        cmd.Parameters.AddWithValue("expiresAt", (object?)expiresAt ?? DBNull.Value);
        return await cmd.ExecuteScalarAsync(ct) is Guid;
    }

    public async Task<bool> RemoveFromTeamAsync(
        Guid personId, string teamSlug, CancellationToken ct)
    {
        const string sql = """
            DELETE FROM identity.team_members m
             USING identity.teams t
             WHERE t.id = m.team_id
               AND m.person_id = @personId
               AND t.slug = @slug
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("slug", teamSlug);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> GrantAsync(
        Guid personId, Permission permission, DateTimeOffset? expiresAt,
        Guid grantedBy, CancellationToken ct)
    {
        // granted_at is bumped on the update so the record answers "when was
        // this last decided", not "when was it first decided" — the second
        // being the one that misleads, by making a grant somebody renewed
        // yesterday look like it has been sitting there since September.
        const string sql = """
            INSERT INTO identity.grants (person_id, permission, expires_at, granted_by)
            SELECT p.id, @permission, @expiresAt, @grantedBy
              FROM identity.people p WHERE p.id = @personId
            ON CONFLICT (person_id, permission) DO UPDATE
                SET expires_at = EXCLUDED.expires_at,
                    granted_by = EXCLUDED.granted_by,
                    granted_at = now()
            RETURNING person_id
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("permission", permission.Value);
        cmd.Parameters.AddWithValue("expiresAt", (object?)expiresAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("grantedBy", grantedBy);
        return await cmd.ExecuteScalarAsync(ct) is Guid;
    }

    public async Task<bool> RevokeGrantAsync(
        Guid personId, Permission permission, CancellationToken ct)
    {
        // Deleted rather than expired-in-place. An admin removing a grant
        // means it should stop counting now, and a row with expires_at set to
        // the present moment is the same thing said less clearly.
        const string sql = """
            DELETE FROM identity.grants
             WHERE person_id = @personId AND permission = @permission
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("permission", permission.Value);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> RevokePersonAsync(
        Guid personId, DateTimeOffset now, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // coalesce keeps the first revocation's timestamp. Re-revoking someone
        // is how an admin finishes a job that half-failed, and it should not
        // quietly rewrite when their access actually ended.
        const string revoke = """
            UPDATE identity.people
               SET revoked_at = coalesce(revoked_at, @now), updated_at = now()
             WHERE id = @id
            RETURNING id
            """;

        await using (var cmd = new NpgsqlCommand(revoke, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", personId);
            cmd.Parameters.AddWithValue("now", now);
            if (await cmd.ExecuteScalarAsync(ct) is not Guid)
            {
                await tx.RollbackAsync(ct);
                return false;
            }
        }

        // Unconditional, not skipped when the person was already revoked. The
        // failure this guards against is the first attempt having written the
        // flag and died before it cut the sessions.
        await RevokeSessionsAsync(conn, tx, personId, now, ct);

        await tx.CommitAsync(ct);
        return true;
    }
}
