using MorganHacks.Applications.Services;
using MorganHacks.Identity.Domain;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// Posting a notice to everybody at an event, and taking one back down.
/// </summary>
/// <remarks>
/// The organizers' half of the portal's announcements screen. It exists
/// because the only way this system could previously tell every hacker
/// anything was a broadcast: several hundred emails that cannot be recalled
/// once lark starts draining them and that take minutes to land. Nobody sends
/// one to move a session by two hours, so the correction gets shouted across a
/// room instead and half the floor never hears it.
/// <para>
/// One permission, <c>announcements.post</c>, on every route here including
/// the list. Reading what has been posted is not a smaller act than posting —
/// the applicants can already read all of it, so there is nothing to protect
/// on the read except the ids, and the ids exist only to be retracted with.
/// Splitting the read onto <c>applications.view</c> would hand the list to a
/// much larger group for no gain at all.
/// </para>
/// <para>
/// <b>Nothing here edits.</b> There is no PUT, and adding one would be a
/// mistake rather than a feature: people have already read the notice, the
/// portal has no unread state and sends no notification, so a body changed in
/// place is a fact that silently moved underneath everybody who acted on the
/// old one. The replacement for an edit is a retraction and a second post,
/// which leaves both in the feed in the order they happened and puts the
/// correction at the top. See <c>0024</c>.
/// </para>
/// <para>
/// The event is named in the URL rather than guessed. "The current event" is a
/// phrase with no unambiguous meaning in a database that holds several — the
/// one being run now, the one taking applications, and the one somebody made
/// last week to test a form are three different rows — and guessing wrong here
/// posts a notice onto a screen nobody is reading, or onto one everybody is.
/// </para>
/// </remarks>
public static class AnnouncementEndpoints
{
    public static IEndpointRouteBuilder MapAnnouncements(this IEndpointRouteBuilder app)
    {
        // Nested under the event for the two that are about one event, and
        // flat for the retraction, which is about one notice and has no need
        // to be told which event it was posted to. The same shape the form
        // builder uses for publish and unpublish.
        app.MapGet("/admin/events/{eventId:guid}/announcements", List)
           .RequirePermission(Permission.AnnouncementsPost);
        app.MapPost("/admin/events/{eventId:guid}/announcements", Post)
           .RequirePermission(Permission.AnnouncementsPost);
        app.MapPost("/admin/announcements/{id:guid}/retract", Retract)
           .RequirePermission(Permission.AnnouncementsPost);

        return app;
    }

    /// <summary>
    /// The whole of what posting one takes.
    /// </summary>
    /// <remarks>
    /// One field, because one sentence is the entire idea. No title, no
    /// scheduled time and nobody to send it to — every one of those turns a
    /// thing somebody types in ten seconds while walking into something they
    /// sit down to compose, and the notice that does not get posted is worse
    /// than the one that is not formatted.
    /// <para>
    /// Nullable and checked in the handler for the reason every other admin
    /// body here is: minimal APIs bind before endpoint filters run, so a
    /// required body answers a request with no session by complaining about
    /// JSON instead of asking them to sign in.
    /// </para>
    /// </remarks>
    public sealed record PostAnnouncementRequest(string? Body);

    /// <summary>The longest a notice may be.</summary>
    /// <remarks>
    /// The same number as the check constraint in <c>0024</c>, and stated in
    /// both places on purpose — the database is what a hand-written INSERT
    /// during the event meets, and this is what a person meets. Generous for
    /// a corridor notice on a phone and small enough that this write endpoint
    /// is not somewhere to put a megabyte.
    /// </remarks>
    private const int BodyLimit = 500;

    /// <summary>
    /// Everything posted for one event, retracted included, newest first.
    /// </summary>
    /// <remarks>
    /// Retracted notices are in this list and marked, which is the opposite of
    /// what the applicant sees. An organizer who took one down at 2pm has to
    /// be able to see that they did: a list that quietly dropped it looks
    /// exactly like one where the retraction never landed, and the next thing
    /// that happens is somebody retracting it again to be sure.
    /// </remarks>
    private static async Task<IResult> List(
        Guid eventId, IAnnouncementStore announcements, CancellationToken ct) =>
        Results.Ok(new
        {
            announcements = (await announcements.ForEventAsync(eventId, ct)).Select(Describe),
        });

    /// <summary>
    /// Posts one. Requires <c>announcements.post</c>.
    /// </summary>
    /// <remarks>
    /// The one thing worth saying here that is not about validation: what goes
    /// in <c>body</c> is read, unchanged and unpersonalised, by every applicant
    /// holding an application for this event. There is no targeting on this
    /// route, no recipient list and no merge fields, so nothing here can leak
    /// one applicant's details to another by accident — but equally nothing
    /// here can stop somebody typing them in, and the console that calls this
    /// is where that has to be said to the person typing.
    /// </remarks>
    private static async Task<IResult> Post(
        Guid eventId,
        PostAnnouncementRequest? request,
        HttpContext http,
        IAnnouncementStore announcements,
        ILogger<PostAnnouncementRequest> log,
        CancellationToken ct)
    {
        var body = request?.Body?.Trim();

        if (string.IsNullOrEmpty(body))
        {
            return Results.BadRequest(new { error = "An announcement needs something to say." });
        }

        if (body.Length > BodyLimit)
        {
            return Results.BadRequest(new
            {
                error = $"An announcement has to fit in {BodyLimit} characters. "
                        + "Anything longer than that is an email.",
            });
        }

        Announcement posted;
        try
        {
            posted = await announcements.PostAsync(eventId, body, http.PersonId(), ct);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23503")
        {
            // The foreign key on event_id. 404 rather than 400: the request is
            // well formed and the event in the path simply is not there, which
            // is what a mistyped id looks like.
            return Results.NotFound(new { error = "No such event." });
        }

        // The ids and never the wording. The row holds what was said, and a
        // second copy in a log is one more place a correction would have to
        // reach. The length is here instead because it is the one thing about
        // the text worth counting later.
        log.LogInformation(
            "An announcement was posted. {actor} {event_id} {announcement_id} {length} {event}",
            http.PersonId(), eventId, posted.Id, body.Length, Events.AnnouncementPosted);

        return Results.Created($"/admin/announcements/{posted.Id}", Describe(posted));
    }

    /// <summary>
    /// Takes one down. Requires <c>announcements.post</c>.
    /// </summary>
    /// <remarks>
    /// A POST rather than a DELETE, because nothing is deleted: the row stays,
    /// carrying who took it down and when, and only the applicants' view of it
    /// changes. A DELETE would make "we never said that" and "we said it and
    /// took it back" the same database state, which is the one distinction
    /// somebody asks about afterwards.
    /// <para>
    /// Idempotent. Retracting a notice that is already down answers 200 with
    /// the row as it stands rather than a conflict — during the ten minutes
    /// this actually gets used, two organizers reaching for the same mistake
    /// is the normal case, and neither of them should be shown an error for
    /// agreeing with the other.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Retract(
        Guid id,
        HttpContext http,
        IAnnouncementStore announcements,
        ILogger<Announcement> log,
        CancellationToken ct)
    {
        var retracted = await announcements.RetractAsync(id, http.PersonId(), ct);

        if (retracted is null)
        {
            return Results.NotFound(new { error = "No such announcement." });
        }

        // Logged on every call, including the second one that changed nothing.
        // Who reached for it is the fact worth having: a retraction two people
        // both went for is a different afternoon from one nobody was sure
        // about, and the row only remembers whoever got there first.
        log.LogInformation(
            "An announcement was retracted. {actor} {event_id} {announcement_id} {event}",
            http.PersonId(), retracted.EventId, retracted.Id, Events.AnnouncementRetracted);

        return Results.Ok(Describe(retracted));
    }

    /// <summary>
    /// The only shape an announcement leaves the admin API in.
    /// </summary>
    /// <remarks>
    /// One projection for both handlers, so a field cannot appear on the list
    /// and be missing from the row a post returns — which is the difference
    /// between a console that can render what it just created and one that has
    /// to reload the page to find out.
    /// <para>
    /// This is the organizers' shape and it carries the retraction. The
    /// applicants' shape is <see cref="PortalEndpoints"/>' and carries
    /// neither that nor <c>postedBy</c>.
    /// </para>
    /// </remarks>
    private static object Describe(Announcement announcement) => new
    {
        id = announcement.Id,
        eventId = announcement.EventId,
        body = announcement.Body,
        postedAt = announcement.PostedAt,
        postedBy = announcement.PostedBy,

        // Said plainly as well as by the timestamp, because the console has to
        // change shape for it and a screen inferring "retracted" from a
        // non-null date is a screen that gets it wrong once.
        retracted = announcement.RetractedAt is not null,
        retractedAt = announcement.RetractedAt,
        retractedBy = announcement.RetractedBy,
    };
}
