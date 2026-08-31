using MorganHacks.Identity.Domain;

namespace MorganHacks.Identity.Services;

/// <summary>
/// Answers what one person may do, right now.
/// </summary>
public sealed class PermissionService(IIdentityStore store, TimeProvider clock)
{
    public async Task<EffectivePermissions> ForAsync(
        Guid personId, CancellationToken ct = default)
    {
        var (memberships, grants, baselines) =
            await store.GetPermissionContextAsync(personId, ct);

        return EffectivePermissions.Resolve(
            memberships, grants, baselines, clock.GetUtcNow());
    }
}
