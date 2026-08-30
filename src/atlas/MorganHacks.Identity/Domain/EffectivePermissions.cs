namespace MorganHacks.Identity.Domain;

/// <summary>A team membership, which may be time-limited.</summary>
public sealed record TeamMembership(string TeamSlug, DateTimeOffset? ExpiresAt);

/// <summary>A permission granted to one person directly, which may be time-limited.</summary>
public sealed record PermissionGrant(Permission Permission, DateTimeOffset? ExpiresAt);

/// <summary>A team and the baseline it confers on its members.</summary>
public sealed record TeamBaseline(string TeamSlug, IReadOnlySet<Permission> Permissions);

/// <summary>
/// What one person is actually allowed to do, right now.
/// </summary>
/// <remarks>
/// Effective permissions are the union of every team baseline the person still
/// holds and every individual grant that has not expired.
/// <para>
/// Additive only. There is deliberately no way to express "this team grants it
/// but this person is denied": subtractive overrides make effective permissions
/// impossible to reason about, and turn "why can this person not see
/// applications" into a search through every rule that touches them. If
/// someone should not have a team's permission, they should not be on that
/// team.
/// </para>
/// </remarks>
public sealed class EffectivePermissions
{
    private readonly IReadOnlySet<Permission> _granted;

    private EffectivePermissions(IReadOnlySet<Permission> granted) => _granted = granted;

    /// <summary>Nobody, allowed nothing. The safe default for an unauthenticated request.</summary>
    public static readonly EffectivePermissions None =
        new(new HashSet<Permission>());

    public static EffectivePermissions Resolve(
        IEnumerable<TeamMembership> memberships,
        IEnumerable<PermissionGrant> grants,
        IEnumerable<TeamBaseline> baselines,
        DateTimeOffset now)
    {
        var baselineBySlug = baselines.ToDictionary(b => b.TeamSlug, b => b.Permissions);
        var effective = new HashSet<Permission>();

        foreach (var membership in memberships)
        {
            if (IsExpired(membership.ExpiresAt, now))
            {
                continue;
            }

            // A membership in a team we have no baseline for grants nothing,
            // rather than throwing. Deleting a team should not break login for
            // everyone who was in it.
            if (baselineBySlug.TryGetValue(membership.TeamSlug, out var permissions))
            {
                effective.UnionWith(permissions);
            }
        }

        foreach (var grant in grants)
        {
            if (!IsExpired(grant.ExpiresAt, now))
            {
                effective.Add(grant.Permission);
            }
        }

        return new EffectivePermissions(effective);
    }

    /// <summary>
    /// The only question calling code should ask. Check permissions, never
    /// roles: <c>Can(Permission.ApplicationsDecide)</c>, never
    /// <c>IsInTeam("registration")</c>. Role checks scattered through endpoints
    /// mean every reorganisation is a code change.
    /// </summary>
    public bool Can(Permission permission) => _granted.Contains(permission);

    public IReadOnlySet<Permission> Granted => _granted;

    /// <summary>Expiry is inclusive: a grant expiring exactly now is spent.</summary>
    private static bool IsExpired(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt is not null && expiresAt <= now;
}
