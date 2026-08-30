using MorganHacks.Identity.Domain;

namespace MorganHacks.Api.Tests;

public class EffectivePermissionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly TeamBaseline Registration = new(
        "registration",
        new HashSet<Permission>
        {
            Permission.ApplicationsView,
            Permission.ApplicationsDecide,
            Permission.ApplicationsViewResume,
        });

    private static readonly TeamBaseline Judges = new(
        "judges",
        new HashSet<Permission> { Permission.JudgingScoreAssigned });

    private static EffectivePermissions Resolve(
        IEnumerable<TeamMembership>? memberships = null,
        IEnumerable<PermissionGrant>? grants = null) =>
        EffectivePermissions.Resolve(
            memberships ?? [],
            grants ?? [],
            [Registration, Judges],
            Now);

    [Fact]
    public void Team_membership_confers_the_whole_baseline()
    {
        var permissions = Resolve(memberships: [new TeamMembership("registration", null)]);

        Assert.True(permissions.Can(Permission.ApplicationsView));
        Assert.True(permissions.Can(Permission.ApplicationsDecide));
        Assert.True(permissions.Can(Permission.ApplicationsViewResume));
    }

    [Fact]
    public void Individual_grants_layer_on_top_of_team_baselines()
    {
        var permissions = Resolve(
            memberships: [new TeamMembership("registration", null)],
            grants: [new PermissionGrant(Permission.EmailSendTemplated, null)]);

        Assert.True(permissions.Can(Permission.ApplicationsView));
        Assert.True(permissions.Can(Permission.EmailSendTemplated));
    }

    [Fact]
    public void Nothing_is_granted_by_default()
    {
        Assert.False(Resolve().Can(Permission.ApplicationsView));
        Assert.False(EffectivePermissions.None.Can(Permission.ApplicationsView));
    }

    [Fact]
    public void Expired_grant_confers_nothing()
    {
        var permissions = Resolve(grants:
        [
            new PermissionGrant(Permission.ApplicationsExport, Now.AddSeconds(-1)),
        ]);

        Assert.False(permissions.Can(Permission.ApplicationsExport));
    }

    [Fact]
    public void Grant_expiring_exactly_now_is_already_spent()
    {
        var permissions = Resolve(grants:
        [
            new PermissionGrant(Permission.ApplicationsExport, Now),
        ]);

        Assert.False(permissions.Can(Permission.ApplicationsExport));
    }

    [Fact]
    public void Expired_team_membership_confers_nothing()
    {
        // The judge case: access should die the day after the event rather
        // than when somebody remembers to remove it.
        var permissions = Resolve(memberships:
        [
            new TeamMembership("judges", Now.AddDays(-1)),
        ]);

        Assert.False(permissions.Can(Permission.JudgingScoreAssigned));
    }

    [Fact]
    public void Membership_in_an_unknown_team_grants_nothing_and_does_not_throw()
    {
        // Deleting a team must not break login for everyone who was in it.
        var permissions = Resolve(memberships: [new TeamMembership("deleted-team", null)]);

        Assert.Empty(permissions.Granted);
    }

    [Fact]
    public void Permissions_from_two_teams_union_rather_than_conflict()
    {
        var permissions = Resolve(memberships:
        [
            new TeamMembership("registration", null),
            new TeamMembership("judges", null),
        ]);

        Assert.True(permissions.Can(Permission.ApplicationsDecide));
        Assert.True(permissions.Can(Permission.JudgingScoreAssigned));
    }
}

public class PermissionParsingTests
{
    [Theory]
    [InlineData("applications.view")]
    [InlineData("  applications.view  ")]
    public void Known_permissions_round_trip_from_the_database(string stored)
    {
        Assert.True(Permission.TryParse(stored, out var permission));
        Assert.Equal(Permission.ApplicationsView, permission);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("applications.delete_everything")]
    [InlineData("Applications.View")]
    public void Unknown_or_empty_values_grant_nothing(string? stored)
    {
        // A row left behind by a removed permission must be ignored, never
        // treated as granting something the code no longer understands.
        Assert.False(Permission.TryParse(stored, out _));
    }

    [Fact]
    public void Every_sensitive_permission_is_a_real_permission()
    {
        Assert.All(Permission.Sensitive, p => Assert.Contains(p, Permission.All));
    }
}
