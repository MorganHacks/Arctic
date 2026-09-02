using System.Net.Mail;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// The people and permissions admin surface.
/// </summary>
/// <remarks>
/// Under /admin because that is what harbor routes, and because the admin
/// screens live there.
/// <para>
/// Every write here is gated with <see cref="RequirePermissionExtensions"/> and
/// nothing else. Two permissions divide the surface: <c>people.manage_teams</c>
/// covers who exists and which teams they are on, <c>people.grant_permissions</c>
/// covers handing out a permission directly. The split is the point — the
/// second is privilege escalation and is on the sensitive list, the first is
/// day-to-day admin.
/// </para>
/// <para>
/// Nothing in this file logs an address. Every line carries person ids, so a
/// log that leaks tells somebody who did what and not who anybody is.
/// </para>
/// </remarks>
public static class PeopleEndpoints
{
    public static IEndpointRouteBuilder MapPeopleAdmin(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin");

        admin.MapGet("/people", ListPeople)
             .RequirePermission(Permission.PeopleView);
        admin.MapGet("/people/{id:guid}", GetPerson)
             .RequirePermission(Permission.PeopleView);
        admin.MapGet("/teams", ListTeams)
             .RequirePermission(Permission.PeopleView);

        admin.MapPost("/people", AddOrganizer)
             .RequirePermission(Permission.PeopleManageTeams);
        admin.MapPost("/people/{id:guid}/teams", JoinTeam)
             .RequirePermission(Permission.PeopleManageTeams);
        admin.MapDelete("/people/{id:guid}/teams/{slug}", LeaveTeam)
             .RequirePermission(Permission.PeopleManageTeams);
        admin.MapPost("/people/{id:guid}/revoke", RevokePerson)
             .RequirePermission(Permission.PeopleManageTeams);

        // The two that hand out a permission directly are gated separately,
        // because being trusted to put somebody on the logistics team is not
        // the same as being trusted to give them PII export on their own.
        admin.MapPost("/people/{id:guid}/grants", AddGrant)
             .RequirePermission(Permission.PeopleGrantPermissions);
        admin.MapDelete("/people/{id:guid}/grants/{permission}", RemoveGrant)
             .RequirePermission(Permission.PeopleGrantPermissions);

        return app;
    }

    /// <summary>
    /// The bodies these endpoints take.
    /// </summary>
    /// <remarks>
    /// Bound as nullable everywhere below, which is not tidiness. Minimal APIs
    /// bind the body before endpoint filters run, so a required body turns a
    /// request with none into a 400 decided before the permission gate ever
    /// looks at it — an unauthenticated caller learning "that route exists and
    /// wants JSON" rather than "sign in". Optional here, checked in the
    /// handler, means authorization answers first.
    /// </remarks>
    public sealed record AddOrganizerRequest(string Email);
    public sealed record JoinTeamRequest(string Slug, DateTimeOffset? ExpiresAt);
    public sealed record GrantRequest(string Permission, DateTimeOffset? ExpiresAt);

    /// <summary>Requires <c>people.view</c>.</summary>
    private static async Task<IResult> ListPeople(
        HttpContext http, IIdentityStore store, CancellationToken ct)
    {
        var people = await store.ListPeopleAsync(ct);

        return Results.Ok(new
        {
            requestedBy = http.PersonId(),
            people = people.Select(p => new
            {
                id = p.Id,
                kind = p.Kind,
                email = p.Email,
                revoked = p.Revoked,
                teams = p.Teams,
            }),
        });
    }

    /// <summary>
    /// One person, with everything needed to explain what they can do.
    /// Requires <c>people.view</c>.
    /// </summary>
    /// <remarks>
    /// Effective permissions are resolved here from the team baselines this
    /// same request already loaded, rather than by asking
    /// <see cref="PermissionService"/> for a second trip to the database. The
    /// rule itself still lives in one place —
    /// <see cref="EffectivePermissions.Resolve"/> — which is the part that
    /// matters, because a screen that disagrees with the gate about what
    /// somebody can do is worse than no screen.
    /// </remarks>
    private static async Task<IResult> GetPerson(
        Guid id, IIdentityStore store, TimeProvider clock, CancellationToken ct)
    {
        var person = await store.FindPersonAsync(id, ct);
        if (person is null)
        {
            return Results.NotFound(new { error = "No such person." });
        }

        var teams = await store.ListTeamsAsync(ct);
        var effective = EffectivePermissions.Resolve(
            person.Teams,
            person.Grants,
            teams.Select(t => new TeamBaseline(t.Slug, t.Permissions)),
            clock.GetUtcNow());

        return Results.Ok(new
        {
            id = person.Id,
            kind = person.Kind,
            email = person.Email,
            revoked = person.Revoked,
            revokedAt = person.RevokedAt,
            teams = person.Teams.Select(t => new
            {
                slug = t.TeamSlug,
                expiresAt = t.ExpiresAt,
            }),
            grants = person.Grants.Select(g => new
            {
                permission = g.Permission.Value,
                expiresAt = g.ExpiresAt,
            }),
            effective = effective.Granted.Select(p => p.Value).OrderBy(p => p),
        });
    }

    /// <summary>
    /// Teams, their baselines, and the permissions that exist at all. Requires
    /// <c>people.view</c>.
    /// </summary>
    /// <remarks>
    /// The catalogue of permissions rides along with the teams because the
    /// console needs both to draw one screen, and because the alternative is a
    /// second copy of the twenty-three strings in TypeScript. That copy drifts
    /// silently: a permission added here and forgotten there is one the API
    /// enforces and no admin can ever grant.
    /// </remarks>
    private static async Task<IResult> ListTeams(IIdentityStore store, CancellationToken ct)
    {
        var teams = await store.ListTeamsAsync(ct);

        return Results.Ok(new
        {
            teams = teams.Select(t => new
            {
                slug = t.Slug,
                name = t.Name,
                permissions = t.Permissions.Select(p => p.Value).OrderBy(p => p),
            }),
            permissions = Permission.All
                .OrderBy(p => p.Value)
                .Select(p => new
                {
                    value = p.Value,
                    // Marked so the console can put friction in front of the
                    // four that either move PII out of the system or change
                    // who is allowed to.
                    sensitive = Permission.Sensitive.Contains(p),
                }),
        });
    }

    /// <summary>Requires <c>people.manage_teams</c>.</summary>
    private static async Task<IResult> AddOrganizer(
        AddOrganizerRequest? request,
        HttpContext http,
        IIdentityStore store,
        ILogger<AddOrganizerRequest> log,
        CancellationToken ct)
    {
        if (!LooksLikeAnAddress(request?.Email))
        {
            return Results.BadRequest(new { error = "That is not an email address." });
        }

        var result = await store.AddOrganizerAsync(request!.Email, ct);

        if (!result.Accepted)
        {
            // 409 rather than 400: the request was well formed and the address
            // is simply spoken for. The two reasons are told apart because
            // they need different actions — one is "you are done", the other
            // is "that person needs a second address".
            return Results.Conflict(new
            {
                error = result.Rejection == AddOrganizerRejection.AddressIsAHackerAccount
                    ? "That address already has a hacker account. An organizer "
                      + "account has to use a different address."
                    : "That address is already an organizer.",
            });
        }

        // The new organizer's id, not their address. They land with no
        // permissions at all until somebody puts them on a team, which is the
        // model working rather than a gap in it.
        log.LogInformation(
            "Organizer added to the allowlist. {actor} {subject} {event}",
            http.PersonId(), result.PersonId, Events.OrganizerAdded);

        return Results.Created($"/admin/people/{result.PersonId}", new
        {
            id = result.PersonId,
        });
    }

    /// <summary>Requires <c>people.manage_teams</c>.</summary>
    private static async Task<IResult> JoinTeam(
        Guid id,
        JoinTeamRequest? request,
        HttpContext http,
        IIdentityStore store,
        TimeProvider clock,
        ILogger<JoinTeamRequest> log,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Slug))
        {
            return Results.BadRequest(new { error = "A team is required." });
        }

        if (AlreadyExpired(request.ExpiresAt, clock))
        {
            return Results.BadRequest(new { error = ExpiryInThePast });
        }

        if (!await store.AddToTeamAsync(id, request.Slug.Trim(), request.ExpiresAt, ct))
        {
            return Results.NotFound(new { error = "No such person, or no such team." });
        }

        log.LogInformation(
            "Team membership changed. {actor} {subject} {team} {event}",
            http.PersonId(), id, request.Slug, Events.TeamChanged);

        return Results.NoContent();
    }

    /// <summary>Requires <c>people.manage_teams</c>.</summary>
    private static async Task<IResult> LeaveTeam(
        Guid id,
        string slug,
        HttpContext http,
        IIdentityStore store,
        ILogger<JoinTeamRequest> log,
        CancellationToken ct)
    {
        if (!await store.RemoveFromTeamAsync(id, slug, ct))
        {
            return Results.NotFound(new { error = "They were not on that team." });
        }

        log.LogInformation(
            "Team membership removed. {actor} {subject} {team} {event}",
            http.PersonId(), id, slug, Events.TeamChanged);

        return Results.NoContent();
    }

    /// <summary>Requires <c>people.grant_permissions</c>.</summary>
    private static async Task<IResult> AddGrant(
        Guid id,
        GrantRequest? request,
        HttpContext http,
        IIdentityStore store,
        TimeProvider clock,
        ILogger<GrantRequest> log,
        CancellationToken ct)
    {
        // The same gate the store uses when reading grants back. Accepting a
        // string nobody recognises would write a row that grants nothing and
        // shows on the screen as though it grants something.
        if (!Permission.TryParse(request?.Permission, out var permission))
        {
            return Results.BadRequest(new { error = "No such permission." });
        }

        if (AlreadyExpired(request!.ExpiresAt, clock))
        {
            return Results.BadRequest(new { error = ExpiryInThePast });
        }

        if (!await store.GrantAsync(id, permission, request.ExpiresAt, http.PersonId(), ct))
        {
            return Results.NotFound(new { error = "No such person." });
        }

        log.LogInformation(
            "Permission granted. {actor} {subject} {permission} {expiresAt} {event}",
            http.PersonId(), id, permission.Value, request.ExpiresAt, Events.GrantChanged);

        return Results.NoContent();
    }

    /// <summary>Requires <c>people.grant_permissions</c>.</summary>
    private static async Task<IResult> RemoveGrant(
        Guid id,
        string permission,
        HttpContext http,
        IIdentityStore store,
        ILogger<GrantRequest> log,
        CancellationToken ct)
    {
        // Unparseable permissions are still removable. A row left behind by a
        // permission the code has dropped grants nothing, but it is visible,
        // and refusing to delete it would leave it there forever.
        if (!await store.RevokeGrantAsync(id, new Permission(permission), ct))
        {
            return Results.NotFound(new { error = "They did not hold that grant." });
        }

        log.LogInformation(
            "Permission removed. {actor} {subject} {permission} {event}",
            http.PersonId(), id, permission, Events.GrantChanged);

        return Results.NoContent();
    }

    /// <summary>
    /// Takes somebody off the allowlist and ends every session they hold.
    /// Requires <c>people.manage_teams</c>.
    /// </summary>
    private static async Task<IResult> RevokePerson(
        Guid id,
        HttpContext http,
        IIdentityStore store,
        TimeProvider clock,
        ILogger<AddOrganizerRequest> log,
        CancellationToken ct)
    {
        // Revoking yourself would work exactly as designed: the sessions die
        // mid-request and the console logs out. It is refused because the
        // person doing it almost never meant it, and because undoing it needs
        // a second admin who may be asleep.
        if (id == http.PersonId())
        {
            return Results.Conflict(new
            {
                error = "You cannot revoke yourself. Ask another admin.",
            });
        }

        if (!await store.RevokePersonAsync(id, clock.GetUtcNow(), ct))
        {
            return Results.NotFound(new { error = "No such person." });
        }

        log.LogInformation(
            "Access revoked and sessions cut. {actor} {subject} {event}",
            http.PersonId(), id, Events.PersonRevoked);

        return Results.NoContent();
    }

    private const string ExpiryInThePast =
        "That expiry has already passed, which would grant nothing at all.";

    /// <summary>
    /// An expiry in the past is refused rather than accepted and ignored.
    /// </summary>
    /// <remarks>
    /// Expiry is inclusive, so a date already gone means the membership or
    /// grant is spent the moment it is written. The screen would show it,
    /// nothing would change, and the admin would go looking for the bug in the
    /// wrong place.
    /// </remarks>
    private static bool AlreadyExpired(DateTimeOffset? expiresAt, TimeProvider clock) =>
        expiresAt is not null && expiresAt <= clock.GetUtcNow();

    /// <summary>
    /// Enough of a check to catch a typo, and no more.
    /// </summary>
    /// <remarks>
    /// Nothing here is a security boundary — an address on the allowlist grants
    /// nothing until somebody signs into it through Google, and Google decides
    /// whether it exists. This only stops a slip of the keyboard becoming a row
    /// that can never match anything.
    /// </remarks>
    private static bool LooksLikeAnAddress(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && MailAddress.TryCreate(email.Trim(), out var parsed)
        && parsed.Host.Contains('.');
}
