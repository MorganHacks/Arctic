using System.Globalization;
using System.Text.Json;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Domain;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// Making the year's event, and setting its dates.
/// </summary>
/// <remarks>
/// Until this file existed an event was made by hand, in psql, once a year.
/// That is the whole explanation for staging having none while a developer's
/// laptop had one: somebody inserted theirs locally during testing and nothing
/// ever inserted anybody else's.
/// <para>
/// Two permissions, split the way the form builder's are. Reading the list is
/// <c>applications.view</c>, because the same list is already inside the forms
/// and applicants responses that every team behind that permission loads —
/// making the standalone list stricter would give two different answers to
/// "which events are there". Writing is <c>events.manage</c>, which super
/// admin holds and nobody else does: an event is the root that forms,
/// applications and campaign segments all hang off, and its registration dates
/// decide who can apply at all.
/// </para>
/// <para>
/// Nothing here deletes, and that is deliberate rather than unfinished. An
/// event with applications attached cannot be deleted in any sense a person
/// would recognise, and nothing else in this system deletes either. An event
/// made by mistake is renamed, or left undated and ignored.
/// </para>
/// </remarks>
public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEvents(this IEndpointRouteBuilder app)
    {
        var events = app.MapGroup("/admin/events");

        events.MapGet("", List)
              .RequirePermission(Permission.ApplicationsView);

        events.MapPost("", Create)
              .RequirePermission(Permission.EventsManage);
        events.MapPut("/{id:guid}", Update)
              .RequirePermission(Permission.EventsManage);

        return app;
    }

    /// <summary>
    /// What creating one takes.
    /// </summary>
    /// <remarks>
    /// Nullable for the reason PeopleEndpoints' and the form builder's are:
    /// minimal APIs bind the body before endpoint filters run, so a required
    /// body answers a request that has none before the permission gate has
    /// looked at it. Optional here and checked in the handler means
    /// authorization answers first.
    /// </remarks>
    public sealed record CreateEventRequest(string? Slug, string? Name);

    /// <summary>
    /// What an update carries, as raw JSON rather than typed fields.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonElement"/> because every field here has three states and
    /// a nullable only has two. An update naming <c>registrationOpensAt</c>
    /// alone must leave the other four dates where they are; an update sending
    /// <c>"endsAt": null</c> has to clear it, because deciding a date and then
    /// un-deciding it is a normal week. Bound as <c>DateTimeOffset?</c> those
    /// two requests are indistinguishable, and the one that loses is the
    /// common one: a console that patches a single field would silently wipe
    /// the rest of the calendar.
    /// <para>
    /// An absent property arrives as <see cref="JsonValueKind.Undefined"/> and
    /// an explicit null as <see cref="JsonValueKind.Null"/>, which is the
    /// whole reason these are not <c>JsonElement?</c> — a nullable value type
    /// collapses both back to null before the handler sees either.
    /// </para>
    /// </remarks>
    public sealed record UpdateEventRequest(
        JsonElement Name,
        JsonElement StartsAt,
        JsonElement EndsAt,
        JsonElement RegistrationOpensAt,
        JsonElement RegistrationClosesAt,
        JsonElement DecisionsAnnouncedAt,
        JsonElement Capacity);

    // ------------------------------------------------------------- reading ---

    /// <summary>Every event, newest first. Requires <c>applications.view</c>.</summary>
    private static async Task<IResult> List(IEventStore events, CancellationToken ct) =>
        Results.Ok(new { events = (await events.ListDetailedAsync(ct)).Select(Describe) });

    // ------------------------------------------------------------- writing ---

    /// <summary>
    /// Makes an event. Requires <c>events.manage</c>.
    /// </summary>
    /// <remarks>
    /// A slug and a name, and nothing else, because that is all anybody knows
    /// in the moment somebody decides next year is happening. Demanding dates
    /// here would mean inventing them, and an invented registration deadline is
    /// one that eventually gets mailed to several hundred people.
    /// </remarks>
    private static async Task<IResult> Create(
        CreateEventRequest? request,
        HttpContext http,
        IEventStore events,
        ILogger<CreateEventRequest> log,
        CancellationToken ct)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Results.BadRequest(new { error = "An event needs a name." });
        }

        if (string.IsNullOrWhiteSpace(request?.Slug))
        {
            return Results.BadRequest(new
            {
                error = "An event needs a slug, the short identifier that goes in links.",
            });
        }

        // Normalised rather than accepted as typed. A trailing space and a
        // capital letter are typing; a slash is a decision, and one that turns
        // into an extra path segment the first time this lands in a URL.
        var slug = EventSlug.Normalise(request.Slug);
        if (slug is null)
        {
            return Results.BadRequest(new { error = SlugRule });
        }

        EventDetail created;
        try
        {
            created = await events.CreateAsync(slug, name, http.PersonId(), ct);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505")
        {
            // The slug's unique index, which has been on the column since the
            // table existed. 409 rather than 400: the request is well formed
            // and the identifier is simply spoken for.
            return Results.Conflict(new
            {
                error = "That slug is already taken by another event.",
            });
        }

        log.LogInformation(
            "Event created. {actor} {event_id} {slug} {event}",
            http.PersonId(), created.Id, created.Slug, Events.EventCreated);

        return Results.Created($"/admin/events/{created.Id}", Describe(created));
    }

    /// <summary>
    /// Sets an event's name, dates and capacity. Requires <c>events.manage</c>.
    /// </summary>
    /// <remarks>
    /// Every date is an instant, and one that says which offset it is in. The
    /// console decides what somebody meant by "registration opens on the 15th
    /// at midnight" before it gets here, because the answer depends on a
    /// timezone this layer has no business guessing — the same rule the form
    /// builder's schedule endpoint follows, and the same rule whose absence
    /// kept <c>rsvp_deadline</c> out of the mail placeholders. What is new here
    /// is that a value arriving without an offset is refused rather than read
    /// as the server's local midnight, which is a wrong answer that looks
    /// exactly like a right one until the day it lands on.
    /// <para>
    /// The slug is not settable. It is what links are built from, and a
    /// renamed identifier is a broken one.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Update(
        Guid id,
        UpdateEventRequest? request,
        HttpContext http,
        IEventStore events,
        ILogger<UpdateEventRequest> log,
        CancellationToken ct)
    {
        // Looked up before anything is written, for the reason the form
        // builder looks a form up: it is the difference between "no such
        // event" and a silent no-op against an id somebody mistyped. The row
        // is also what the two ordering checks below are measured against,
        // since an update naming one date has to be judged with the four it
        // did not name.
        var existing = await events.ByIdAsync(id, ct);
        if (existing is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        if (request is null)
        {
            return Results.Ok(Describe(existing));
        }

        string? name = null;
        if (request.Name.ValueKind is not JsonValueKind.Undefined)
        {
            if (request.Name.ValueKind is not JsonValueKind.String
                || string.IsNullOrWhiteSpace(request.Name.GetString()))
            {
                return Results.BadRequest(new { error = "An event needs a name." });
            }

            name = request.Name.GetString()!.Trim();
        }

        if (!TryInstant(request.StartsAt, "The start date", out var startsAt, out var refused)
            || !TryInstant(request.EndsAt, "The end date", out var endsAt, out refused)
            || !TryInstant(
                request.RegistrationOpensAt,
                "The date registration opens", out var opens, out refused)
            || !TryInstant(
                request.RegistrationClosesAt,
                "The date registration closes", out var closes, out refused)
            || !TryInstant(
                request.DecisionsAnnouncedAt,
                "The date decisions are announced", out var announced, out refused)
            || !TryCapacity(request.Capacity, out var capacity, out refused))
        {
            return Results.BadRequest(new { error = refused });
        }

        // Checked against what the row will hold afterwards rather than
        // against what this request happened to name. Both of these are
        // silent when they are wrong: an event that ends before it starts
        // reads as a one-line typo on a screen, and a registration window that
        // closes before it opens is a form nobody can ever answer, reported a
        // week later as "the link is broken".
        var (endsFrom, startsFrom) =
            (Effective(endsAt, existing.EndsAt), Effective(startsAt, existing.StartsAt));
        if (endsFrom < startsFrom)
        {
            return Results.BadRequest(new { error = "An event cannot end before it starts." });
        }

        var (closesFrom, opensFrom) = (
            Effective(closes, existing.RegistrationClosesAt),
            Effective(opens, existing.RegistrationOpensAt));
        if (closesFrom < opensFrom)
        {
            return Results.BadRequest(new
            {
                error = "Registration cannot close before it opens.",
            });
        }

        var updated = await events.UpdateAsync(id, new EventEdit
        {
            Name = name,
            StartsAt = startsAt,
            EndsAt = endsAt,
            RegistrationOpensAt = opens,
            RegistrationClosesAt = closes,
            DecisionsAnnouncedAt = announced,
            Capacity = capacity,
        }, ct);

        if (updated is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        // Which fields moved, never what they moved to. The row itself is the
        // record of the values; what a log adds is who touched what, and a
        // registration deadline in a log line is one more copy of a date that
        // has to be corrected in two places.
        log.LogInformation(
            "Event updated. {actor} {event_id} {fields} {event}",
            http.PersonId(), id, Changed(name, startsAt, endsAt, opens, closes, announced, capacity),
            Events.EventUpdated);

        return Results.Ok(Describe(updated));
    }

    // ------------------------------------------------------------- reading a field ---

    /// <summary>
    /// The rule a slug has to satisfy, as one sentence somebody can act on.
    /// </summary>
    /// <remarks>
    /// Named once because it is also what <see cref="EventSlug"/> enforces and
    /// what the check constraint in 0020 enforces, and a rule stated in three
    /// places drifts in two of them.
    /// </remarks>
    private const string SlugRule =
        "A slug is between 2 and 40 characters, and can hold lower case letters, "
        + "digits and single hyphens between them.";

    /// <summary>
    /// Reads a date field: absent, cleared, or an instant.
    /// </summary>
    /// <remarks>
    /// An offset is required rather than assumed. "2027-01-15T00:00:00" with
    /// nothing after it is midnight somewhere, and .NET resolves that to the
    /// machine's local zone — UTC in a container, something else on a laptop,
    /// and in both cases a different calendar day for the people the date
    /// exists for. Refusing it is the only answer that cannot be quietly
    /// wrong.
    /// </remarks>
    private static bool TryInstant(
        JsonElement field, string label, out Patch<DateTimeOffset> patch, out string? error)
    {
        patch = default;
        error = null;

        switch (field.ValueKind)
        {
            case JsonValueKind.Undefined:
                return true;

            case JsonValueKind.Null:
                patch = Patch<DateTimeOffset>.To(null);
                return true;

            case JsonValueKind.String when TryRead(field.GetString(), out var instant):
                patch = Patch<DateTimeOffset>.To(instant);
                return true;

            case JsonValueKind.String:
                error = $"{label} needs a time zone offset. Without one, midnight "
                        + "lands on a different day for different people.";
                return false;

            default:
                error = $"{label} could not be read as a date and time.";
                return false;
        }
    }

    /// <summary>An instant, and only if the text really carries one.</summary>
    private static bool TryRead(string? text, out DateTimeOffset instant)
    {
        instant = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // A bare date has no instant in it at all, so it is refused here
        // rather than parsed into somebody's local midnight.
        var separator = text.IndexOf("T", StringComparison.OrdinalIgnoreCase);
        if (separator < 0)
        {
            return false;
        }

        var time = text.AsSpan(separator + 1);
        var carriesOffset = time.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
                            || time.Contains('+')
                            || time.Contains('-');

        return carriesOffset
               && DateTimeOffset.TryParse(
                   text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out instant);
    }

    /// <summary>Reads the capacity: absent, cleared, or a count of people.</summary>
    /// <remarks>
    /// The target confirmations are tracked against, so zero and below are not
    /// a smaller event, they are a typo. Cleared is a real answer and means
    /// the room size is still unknown.
    /// </remarks>
    private static bool TryCapacity(JsonElement field, out Patch<int> patch, out string? error)
    {
        patch = default;
        error = null;

        switch (field.ValueKind)
        {
            case JsonValueKind.Undefined:
                return true;

            case JsonValueKind.Null:
                patch = Patch<int>.To(null);
                return true;

            case JsonValueKind.Number when field.TryGetInt32(out var capacity):
                if (capacity < 1)
                {
                    error = "Capacity must be at least one person.";
                    return false;
                }

                patch = Patch<int>.To(capacity);
                return true;

            default:
                error = "Capacity must be a whole number of people.";
                return false;
        }
    }

    /// <summary>What a field will hold once this update lands.</summary>
    private static DateTimeOffset? Effective(Patch<DateTimeOffset> patch, DateTimeOffset? current) =>
        patch.Present ? patch.Value : current;

    // ------------------------------------------------------------- shaping ---

    /// <summary>The names of the fields an update touched, for the log line.</summary>
    private static string Changed(
        string? name,
        Patch<DateTimeOffset> startsAt,
        Patch<DateTimeOffset> endsAt,
        Patch<DateTimeOffset> opens,
        Patch<DateTimeOffset> closes,
        Patch<DateTimeOffset> announced,
        Patch<int> capacity)
    {
        var touched = new List<string>(7);

        if (name is not null)
        {
            touched.Add("name");
        }

        if (startsAt.Present)
        {
            touched.Add("startsAt");
        }

        if (endsAt.Present)
        {
            touched.Add("endsAt");
        }

        if (opens.Present)
        {
            touched.Add("registrationOpensAt");
        }

        if (closes.Present)
        {
            touched.Add("registrationClosesAt");
        }

        if (announced.Present)
        {
            touched.Add("decisionsAnnouncedAt");
        }

        if (capacity.Present)
        {
            touched.Add("capacity");
        }

        return string.Join(',', touched);
    }

    private static object Describe(EventDetail e) => new
    {
        id = e.Id,
        slug = e.Slug,
        name = e.Name,
        startsAt = e.StartsAt,
        endsAt = e.EndsAt,
        registrationOpensAt = e.RegistrationOpensAt,
        registrationClosesAt = e.RegistrationClosesAt,
        decisionsAnnouncedAt = e.DecisionsAnnouncedAt,
        capacity = e.Capacity,
        createdAt = e.CreatedAt,
        createdBy = e.CreatedBy,
    };
}
