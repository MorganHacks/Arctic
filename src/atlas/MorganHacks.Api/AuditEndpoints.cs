using System.Text.Json;
using MorganHacks.Audit;
using MorganHacks.Identity.Domain;

namespace MorganHacks.Api;

/// <summary>
/// Reading the permission audit trail.
/// </summary>
/// <remarks>
/// Read-only, and there is no companion file with the writes in it. Entries
/// are written by database triggers inside the transaction that changed
/// somebody's access — see <c>0009_audit.sql</c> — so there is no endpoint
/// that appends to the trail, none that edits it, and none that deletes from
/// it. The database refuses the last two outright.
/// <para>
/// Gated on <c>audit.view</c>, which only the super-admin baseline confers.
/// The trail is a map of who holds what: it names every person with a
/// sensitive permission and when they got it, which is the reconnaissance step
/// for anyone who wants one of those accounts.
/// </para>
/// <para>
/// Nothing here returns an address. Entries carry person ids, and the console
/// resolves those against /admin/people, which is gated separately on
/// <c>people.view</c> — so the trail cannot become a second, ungated way to
/// read the allowlist.
/// </para>
/// </remarks>
public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditTrail(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/admin")
           .MapGet("/audit", Read)
           .RequirePermission(Permission.AuditView);

        return app;
    }

    /// <summary>
    /// The trail, newest first. Requires <c>audit.view</c>.
    /// </summary>
    /// <remarks>
    /// <paramref name="subject"/> answers "what has been done to this person"
    /// and <paramref name="actor"/> answers "what has this person been doing".
    /// Both are on one endpoint rather than two because the second question is
    /// almost always asked about somebody who turned up in the answer to the
    /// first, and a reviewer following that thread should not have to change
    /// screens to do it.
    /// <para>
    /// A malformed uuid is a 400 rather than an ignored filter. Silently
    /// dropping it would answer a narrow question with the whole table, which
    /// reads as "this person did all of that".
    /// </para>
    /// </remarks>
    private static async Task<IResult> Read(
        IAuditTrail trail,
        CancellationToken ct,
        Guid? subject = null,
        Guid? actor = null,
        long? before = null,
        int limit = 100)
    {
        var entries = await trail.ReadAsync(
            new AuditQuery(subject, actor, before, limit), ct);

        return Results.Ok(new
        {
            entries = entries.Select(e => new
            {
                id = e.Id,
                occurredAt = e.OccurredAt,
                action = e.Action,
                // Null where nobody was behind it, and passed through as null
                // rather than smoothed into "system". The console says
                // "no actor" and means it; a label invented here would look
                // like a service account that exists.
                actorId = e.ActorId,
                subjectId = e.SubjectId,
                subjectTeam = e.SubjectTeam,
                target = e.Target,
                expiresAt = e.ExpiresAt,
                detail = JsonSerializer.Deserialize<JsonElement>(e.Detail),
            }),

            // Where the next page starts, and null once a page comes back
            // empty. A cursor rather than a page number because the table only
            // grows at the newest end: an offset would shift under a reader
            // while an incident is still producing entries, showing one entry
            // twice and skipping another.
            nextBefore = entries.Count == 0 ? null : (long?)entries[^1].Id,
        });
    }
}
