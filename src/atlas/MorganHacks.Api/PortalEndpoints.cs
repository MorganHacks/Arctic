using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Data;
using MorganHacks.Applications.Services;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;
using MorganHacks.Observability;
using MorganHacks.Features;

namespace MorganHacks.Api;

/// <summary>
/// What an applicant can see and change about their own application.
/// </summary>
/// <remarks>
/// Session-gated rather than permission-gated. An applicant holds no
/// permissions and never will; what makes these endpoints safe is that every
/// one of them reads by the session's person id and none of them accepts an id
/// from the caller. There is no <c>/portal/{id}</c> here, and there should
/// never be one — the moment a route takes an application id, "can I see this"
/// becomes a check somebody has to remember to write.
/// <para>
/// Two things must not leak out of this file:
/// </para>
/// <list type="bullet">
/// <item>
/// The internal status. Every status an applicant reads goes through
/// <see cref="ApplicantView"/>, which is what lets a reviewer decide on Tuesday
/// and the team announce on Friday.
/// </item>
/// <item>
/// Rendered message bodies. The history says what was sent and what became of
/// it, never what it said.
/// </item>
/// </list>
/// <para>
/// <see cref="Announcements"/> returns text an organizer typed, which reads
/// like an exception to the second of those and is not one. The rule there is
/// about a <em>rendered message</em> — one addressed to one person, whose body
/// is a decision letter or a live sign-in link. An announcement has no
/// recipient at all: one row is shown identically to everybody at the event,
/// nothing merges anything into it, and it is the only thing in the system
/// written specifically to be read by all of them.
/// </para>
/// <para>
/// Two routes here move an application's status, and they are the only ones
/// that ever should. <see cref="AnswerRsvp"/> takes a spot or gives it back;
/// <see cref="Withdraw"/> closes the application at the applicant's own
/// request, which is the thing an RSVP cannot say for anybody who was never
/// offered a spot to turn down. Both do it through
/// <see cref="IApplicationStore.TransitionAsync"/> like every other writer in
/// the system — so the lifecycle table judges the move and the trail records
/// the applicant as the actor. An applicant is not a special case of the audit
/// story; they are a participant in it.
/// </para>
/// <para>
/// Nothing here logs an address, a name or an answer. Person ids only, like
/// the rest of the codebase — see <c>Redaction.SensitiveKeys</c> for the net
/// underneath that rule.
/// </para>
/// </remarks>
public static class PortalEndpoints
{
    public static IEndpointRouteBuilder MapPortal(this IEndpointRouteBuilder app)
    {
        // The flag sits outside the session check on purpose. When the portal is off
        // it is off for everybody, signed in or not, and the answer is 404 -- the same
        // answer a route that was never built gives.
        var portal = app.MapGroup("/portal")
            .RequireFeature(Flags.HackerPortal)
            .RequireSession();

        portal.MapGet("/me", Me);
        portal.MapPatch("/profile", SaveProfile);
        portal.MapPost("/rsvp", AnswerRsvp);
        portal.MapGet("/messages", Messages);
        portal.MapGet("/announcements", Announcements);
        portal.MapPost("/announcements/{id:guid}/vote", VoteAnnouncement);
        portal.MapPut("/announcements/{id:guid}/reaction", ReactToAnnouncement);
        portal.MapDelete("/announcements/{id:guid}/reaction", RemoveAnnouncementReaction);
        portal.MapGet("/check-in", CheckIn);

        // POST rather than DELETE, and no id in the path. There is no resource
        // here to address: the application is whichever one the session's
        // person owns, and a verb keeps this route the same shape as the other
        // write beside it.
        portal.MapPost("/withdraw", Withdraw);
        // Registered against this group rather than a second one, so the resume
        // routes inherit the same feature flag and the same session gate as
        // every route above. The handlers live in PortalResumeEndpoints only
        // because this file is long and shared.
        portal.MapPortalResume();

        return app;
    }

    /// <summary>
    /// The six fields an applicant owns.
    /// </summary>
    /// <remarks>
    /// Nullable, and checked in the handler rather than required by the
    /// binder, for the same reason as the admin bodies: minimal APIs bind
    /// before endpoint filters run, so a required body answers a request with
    /// no session with "that route wants JSON" instead of "sign in".
    /// </remarks>
    public sealed record ProfileRequest(
        string? FirstName,
        string? LastName,
        string? School,
        string? ShirtSize,
        string? DietaryNeeds,
        string? AccessibilityNeeds);

    /// <summary>
    /// Taking a spot, or giving it back.
    /// </summary>
    /// <remarks>
    /// One verb — <c>confirm</c> or <c>decline</c> — and nullable for the same
    /// binder reason as <see cref="ProfileRequest"/>.
    /// <para>
    /// Deliberately not the stored status. An applicant sending
    /// <c>"confirmed"</c> would mean the wire spelling had reached a screen,
    /// and the next thing to reach a screen is whichever other status somebody
    /// guesses is accepted here.
    /// </para>
    /// </remarks>
    public sealed record RsvpRequest(string? Answer);

    /// <summary>
    /// Their application, in the words they are allowed to read.
    /// </summary>
    /// <remarks>
    /// Answers 200 with a null application for somebody who has not started
    /// one, rather than 404. They are signed in and this is their portal; the
    /// absence of an application is a state of the page, not a missing
    /// resource.
    /// </remarks>
    private static async Task<IResult> Me(
        HttpContext http, IApplicantPortalStore store, CancellationToken ct)
    {
        var application = await store.FindForPersonAsync(http.PersonId(), ct);

        if (application is null)
        {
            return Results.Ok(new { application = (object?)null });
        }

        return Results.Ok(new { application = Describe(application) });
    }

    /// <summary>
    /// Updates the profile, and only while it is still theirs to update.
    /// </summary>
    /// <remarks>
    /// The store decides whether the write may happen, in the same statement
    /// that performs it. What this handler adds is the sentence explaining a
    /// refusal — a disabled field with no reason is the thing that generates
    /// the email this portal exists to prevent.
    /// </remarks>
    private static async Task<IResult> SaveProfile(
        ProfileRequest? request,
        HttpContext http,
        IApplicantPortalStore store,
        ILogger<ProfileRequest> log,
        CancellationToken ct)
    {
        if (request is null)
        {
            return Results.BadRequest(new { error = "Nothing to save." });
        }

        var personId = http.PersonId();
        var current = await store.FindForPersonAsync(personId, ct);

        if (current is null)
        {
            return Results.Conflict(new
            {
                error = "You have not started an application yet.",
            });
        }

        if (!ProfileEditing.IsOpen(current.Status))
        {
            // The store would refuse this anyway. Checking here as well is
            // what turns the refusal into the right sentence rather than a
            // bare 409, and the wording never says which decided state it is.
            return Results.Conflict(new
            {
                error = ProfileEditing.WhyClosed(current.Status),
            });
        }

        // Required even on an unsubmitted application, which looks stricter
        // than it needs to be. It is not: these three are NOT NULL the moment
        // the application stops being a draft, enforced by the completeness
        // constraint, so allowing them to be cleared here only buys the
        // applicant a save that makes their next submit fail.
        foreach (var (field, value) in new[]
                 {
                     ("first name", request.FirstName),
                     ("last name", request.LastName),
                     ("school", request.School),
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Results.BadRequest(new { error = $"Your {field} is required." });
            }
        }

        if (!ShirtSizes.TryNormalise(request.ShirtSize, out var shirtSize))
        {
            return Results.BadRequest(new { error = "That is not a shirt size we order." });
        }

        if (TooLong(request.DietaryNeeds) || TooLong(request.AccessibilityNeeds)
            || TooLong(request.FirstName, NameLimit) || TooLong(request.LastName, NameLimit)
            || TooLong(request.School, NameLimit))
        {
            // Bounded because these columns are unbounded text on a route open
            // to anyone with an account. Generous enough that a real answer
            // never hits it.
            return Results.BadRequest(new { error = "That answer is longer than we can store." });
        }

        var profile = new ApplicantProfile
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            School = request.School,
            ShirtSize = shirtSize,
            DietaryNeeds = request.DietaryNeeds,
            AccessibilityNeeds = request.AccessibilityNeeds,
        };

        var saved = await store.SaveProfileAsync(personId, profile, ct);

        if (saved is not ProfileSave.Saved)
        {
            // Lost the race against a decision landing between the read above
            // and this write. Rare, and the honest answer is the same one the
            // check above gives.
            return Results.Conflict(new
            {
                error = saved is ProfileSave.NoApplication
                    ? "You have not started an application yet."
                    : ProfileEditing.WhyClosed(current.Status)
                      ?? "These details are locked.",
            });
        }

        // The person id and nothing else. Which fields changed is not worth
        // knowing at the cost of a log line that names somebody's dietary
        // requirements.
        log.LogInformation("An applicant updated their profile. {PersonId}", personId);

        var updated = await store.FindForPersonAsync(personId, ct);
        return updated is null
            ? Results.Ok(new { application = (object?)null })
            : Results.Ok(new { application = Describe(updated) });
    }

    /// <summary>
    /// Takes the spot, or gives it back.
    /// </summary>
    /// <remarks>
    /// The one write on this API that moves an application's status, and it is
    /// deliberately the narrowest possible one. Three separate things make it
    /// safe, and none of them is the button on the screen:
    /// <list type="bullet">
    /// <item>
    /// <b>It can only ever be their own application.</b> The id passed to
    /// <see cref="IApplicationStore.TransitionAsync"/> is not read from the
    /// request — there is no field it could come from. It comes from
    /// <see cref="IApplicantPortalStore.FindForPersonAsync"/>, whose every
    /// statement narrows on the session's person id, so RSVPing for somebody
    /// else is not a check that can be forgotten. There is nothing to forget.
    /// </item>
    /// <item>
    /// <b>The move goes through the lifecycle.</b>
    /// <see cref="StatusTransition"/> already permits <c>accepted</c> to
    /// <c>confirmed</c> or <c>declined</c> and nothing else to either, and
    /// <c>TransitionAsync</c> re-reads the status under a row lock before
    /// judging it. So a confirm from any other status is refused by the table
    /// rather than by a condition written here, and it stays refused when an
    /// organizer moves the row between the read below and this write.
    /// </item>
    /// <item>
    /// <b>The trail names the applicant.</b> <c>actorId</c> is the session's
    /// person, so <c>status_history.actor_id</c> says who did it. Writing the
    /// status any other way would record a null actor, which reads as a
    /// hand-fixed row and is permanently unattributable.
    /// </item>
    /// </list>
    /// <para>
    /// The deadline is the one rule <c>TransitionAsync</c> cannot know, so it
    /// is checked here, on the write, against the row this request just read —
    /// not against anything the caller sent, and not by leaving the buttons
    /// off the page. A portal tab left open past the deadline gets a refusal,
    /// which is the whole point of having one.
    /// </para>
    /// <para>
    /// <b>Declining is final.</b> <c>StatusTransition</c> lists nothing after
    /// <c>declined</c>, so there is no undo to offer and this endpoint does not
    /// invent one. That is the lifecycle's decision rather than this file's:
    /// releasing a spot is what moves the waitlist, and a portal that could
    /// silently take it back would be handing out a place that has already
    /// been given to somebody else. Somebody who declines by accident emails
    /// us, and an organizer decides — with a record of both.
    /// </para>
    /// </remarks>
    private static async Task<IResult> AnswerRsvp(
        RsvpRequest? request,
        HttpContext http,
        IApplicantPortalStore store,
        IApplicationStore applications,
        ILogger<RsvpRequest> log,
        CancellationToken ct)
    {
        if (!Rsvp.TryParseAnswer(request?.Answer, out var answer))
        {
            return Results.BadRequest(new { error = "Tell us whether you are coming." });
        }

        var personId = http.PersonId();
        var current = await store.FindForPersonAsync(personId, ct);

        if (current is null)
        {
            return Results.Conflict(new
            {
                error = "You have not started an application yet.",
            });
        }

        // now() once, so the sentence explaining a refusal is judged against
        // the same instant the refusal was.
        var now = DateTimeOffset.UtcNow;
        var closed = Rsvp.WhyClosed(
            current.Status, current.DecisionsAnnounced, current.RsvpDeadline, now);

        if (closed is not null)
        {
            return Results.Conflict(new { error = closed });
        }

        StatusChange change;
        try
        {
            // No reason and no batch id. A reason is a sentence somebody wrote
            // about an applicant and there is nobody here to write one; a batch
            // id would make one person answering indistinguishable from one of
            // four hundred rows an organizer moved.
            change = await applications.TransitionAsync(
                current.Id, Rsvp.Target(answer), actorId: personId, ct: ct);
        }
        catch (InvalidTransitionException)
        {
            // Lost the race against an organizer moving the row between the
            // read above and this write. Rare, and the honest answer is
            // whatever is true now rather than what was true a moment ago.
            var settled = await store.FindForPersonAsync(personId, ct);

            return Results.Conflict(new
            {
                error = settled is null
                    ? "You have not started an application yet."
                    : Rsvp.WhyClosed(
                        settled.Status, settled.DecisionsAnnounced,
                        settled.RsvpDeadline, DateTimeOffset.UtcNow)
                      ?? "That could not be saved.",
            });
        }

        // The person id and the two statuses, the same fields
        // ApplicantEndpoints logs for the organizers' side. Its own event name
        // rather than application.status_changed, because that one is
        // documented as an organizer moving somebody and is watched for a
        // volume that means "a script is running" — four hundred applicants
        // answering the evening decisions go out is the system working, and it
        // must not read as the alarm.
        log.LogInformation(
            "An applicant answered their RSVP. {PersonId} {from} {to} {event}",
            personId, change.From?.ToWire(), change.To.ToWire(), Events.RsvpAnswered);

        // Re-read rather than patched locally, so the screen redraws from the
        // same projection every other route serves. The lifecycle timestamps
        // this move stamped were the trigger's to write, and guessing them
        // here would be a second opinion about when somebody answered.
        var updated = await store.FindForPersonAsync(personId, ct);
        return updated is null
            ? Results.Ok(new { application = (object?)null })
            : Results.Ok(new { application = Describe(updated) });
    }

    /// <summary>
    /// Closes the application, because the applicant says so.
    /// </summary>
    /// <remarks>
    /// The other write that moves a status, and it exists because
    /// <see cref="AnswerRsvp"/> only speaks for people we already accepted.
    /// Everybody else — waiting on a decision, still filling the form in,
    /// confirmed and now unable to come — had no way to tell us at all, and
    /// the cost of that silence is not symmetric: an accepted seat nobody can
    /// give back is a seat the waitlist never gets, food ordered for a chair
    /// that stays empty, and a fortnight of reminder emails to somebody who
    /// told us as loudly as the portal allowed.
    /// <para>
    /// Everything that makes <see cref="AnswerRsvp"/> safe makes this safe, in
    /// the same three ways and for the same reasons: the id comes from
    /// <see cref="IApplicantPortalStore.FindForPersonAsync"/> and never from
    /// the request, so there is no id to forget to check; the move goes
    /// through <see cref="StatusTransition"/>, which already says exactly which
    /// statuses may become <c>withdrawn</c> and re-reads the row under a lock
    /// before judging it; and <c>actorId</c> is the session's person, so
    /// <c>status_history.actor_id</c> says who did it rather than leaving the
    /// null that reads as a hand-fixed row forever after.
    /// </para>
    /// <para>
    /// <b>There is no body.</b> Nothing about this request varies — an
    /// applicant has one application and one thing to say about it — and a
    /// route that took a field would be a route somebody could send an id in.
    /// The second ask before it happens is the portal's, where the person is,
    /// rather than a word echoed back to a server that cannot tell a deliberate
    /// press from a repeated one anyway.
    /// </para>
    /// <para>
    /// <b>Asking twice is not an error.</b> An application already withdrawn
    /// answers 200 with the same projection every other route serves, and
    /// writes nothing. A double submit, a retried request and a back button
    /// all look like this, and answering the second one with a red box would
    /// tell somebody their withdrawal failed at the exact moment it did not.
    /// It costs nothing to be sure of: the state they asked for is the state
    /// they are in. Refusing the write is still the right answer everywhere
    /// else, including <c>declined</c> — that one released a spot through the
    /// RSVP and is a different event with a different consequence, and a
    /// history row saying somebody withdrew when they declined would be this
    /// endpoint rewriting what happened.
    /// </para>
    /// <para>
    /// <b>It is one way.</b> <see cref="StatusTransition"/> lists nothing after
    /// <c>withdrawn</c>, so this endpoint has no undo to offer and does not
    /// invent one — the same decision, made in the same place, as the one that
    /// makes declining final. Somebody who withdraws by accident emails us and
    /// an organizer decides, with a record of both. What this file owes them in
    /// exchange is that the portal says so plainly before the press, which is
    /// the confirmation step on the screen.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Withdraw(
        HttpContext http,
        IApplicantPortalStore store,
        IApplicationStore applications,
        // Categorised on the application rather than on a request record,
        // which is what the other writers here name, because this route has no
        // body to name it after. The same choice ApplicantEndpoints makes.
        ILogger<ApplicantApplication> log,
        CancellationToken ct)
    {
        var personId = http.PersonId();
        var current = await store.FindForPersonAsync(personId, ct);

        if (current is null)
        {
            return Results.Conflict(new
            {
                error = "You have not started an application yet.",
            });
        }

        if (Withdrawal.AlreadyWithdrawn(current.Status))
        {
            // Nothing written and nothing logged: the second arrival of one
            // request is not a second withdrawal, and a trail that recorded it
            // as one would be counting an applicant's clicks as decisions.
            return Results.Ok(new { application = Describe(current) });
        }

        var closed = Withdrawal.WhyClosed(current.Status, current.DecisionsAnnounced);

        if (closed is not null)
        {
            return Results.Conflict(new { error = closed });
        }

        StatusChange change;
        try
        {
            // No reason and no batch id, for the reasons AnswerRsvp gives: a
            // reason is a sentence somebody wrote about an applicant and there
            // is nobody here to write one, and a batch id would make one
            // person leaving indistinguishable from a row an organizer moved
            // in a set of four hundred.
            change = await applications.TransitionAsync(
                current.Id, ApplicationStatus.Withdrawn, actorId: personId, ct: ct);
        }
        catch (InvalidTransitionException)
        {
            // Lost the race against an organizer moving the row between the
            // read above and this write — an acceptance landing, or the
            // check-in desk. Rare, and the honest answer is whatever is true
            // now rather than what was true a moment ago.
            var settled = await store.FindForPersonAsync(personId, ct);

            if (settled is not null && Withdrawal.AlreadyWithdrawn(settled.Status))
            {
                // Two of their own requests crossed. The state they asked for
                // is the state they have, so this is the idempotent answer
                // above arriving a moment later than it might have.
                return Results.Ok(new { application = Describe(settled) });
            }

            return Results.Conflict(new
            {
                error = settled is null
                    ? "You have not started an application yet."
                    : Withdrawal.WhyClosed(settled.Status, settled.DecisionsAnnounced)
                      ?? "That could not be saved.",
            });
        }

        // The person id and the two statuses, the same fields the RSVP line
        // carries and for the same reason: what happened is on the history
        // row, behind a permission, and this line exists so an absence or a
        // cluster can be alerted on without reading anybody's application.
        log.LogInformation(
            "An applicant withdrew their application. {PersonId} {from} {to} {event}",
            personId, change.From?.ToWire(), change.To.ToWire(), Events.ApplicationWithdrawn);

        // Re-read rather than patched locally, so the screen redraws from the
        // same projection every other route serves — including the profile
        // lock and the RSVP panel, both of which this one move closes.
        var updated = await store.FindForPersonAsync(personId, ct);
        return updated is null
            ? Results.Ok(new { application = (object?)null })
            : Results.Ok(new { application = Describe(updated) });
    }

    /// <summary>
    /// Every email we have sent them.
    /// </summary>
    /// <remarks>
    /// Exists so "I never got it" has an answer. Somebody who can see that
    /// their decision email bounced on the 4th knows to check their spam
    /// folder or tell us their address changed, and stops needing an organizer
    /// to look it up for them.
    /// </remarks>
    private static async Task<IResult> Messages(
        HttpContext http, MessageQueue queue, CancellationToken ct)
    {
        var history = await queue.HistoryForPersonAsync(http.PersonId(), ct: ct);

        return Results.Ok(new
        {
            messages = history.Select(m => new
            {
                id = m.Id,
                subject = m.Subject,
                // Queued rather than sent, because a message still in the
                // queue has no sent_at and would otherwise show no date at
                // all — which reads as "we never wrote to you".
                at = m.SentAt ?? m.QueuedAt,
                delivery = DeliveryView.Describe(m.Status),
            }),
        });
    }

    /// <summary>
    /// What the team has told everybody at their event, newest first.
    /// </summary>
    /// <remarks>
    /// Exists because until now the only way this system could tell every
    /// hacker anything was to mail them, and nobody sends four hundred emails
    /// to move a session by two hours. So the correction gets shouted across a
    /// room and half the floor never hears it.
    /// <para>
    /// <b>Who may read these, stated plainly: anybody signed in who holds an
    /// application for that event, and nobody else.</b> That is enforced by
    /// the shape of the query rather than by a check here — see
    /// <see cref="IApplicantPortalStore.AnnouncementsForPersonAsync"/>, which
    /// takes no event id at all and resolves it from the session's own
    /// application. Somebody signed in with no application gets an empty list,
    /// and last year's applicant sees last year's event.
    /// </para>
    /// <para>
    /// The other half of the leak question is not this file's to enforce and
    /// is worth saying anyway: one row is shown identically to every applicant
    /// at the event, so a notice must never contain anything true of only one
    /// of them. Nothing on this route personalises anything — there are no
    /// merge fields here and no recipient — so the text comes back exactly as
    /// an organizer typed it, which means the only way one of these leaks
    /// something is if a person put it there.
    /// </para>
    /// <para>
    /// The one route in this file that returns words an organizer wrote rather
    /// than words the codebase chose. Everywhere else the API sends the
    /// sentence and the portal renders it, precisely so a screen cannot invent
    /// its own mapping; here the sentence <em>is</em> the data, and the rule it
    /// replaces that with is that the portal never edits it.
    /// </para>
    /// <para>
    /// Answers 200 with an empty list for somebody with no application, like
    /// every other read here. They are signed in and this is their portal.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Announcements(
        HttpContext http, IApplicantPortalStore store,
        PostgresAnnouncementResponseStore responses, PostgresAnnouncementReactionStore reactions, CancellationToken ct)
    {
        var posted = await store.AnnouncementsForPersonAsync(http.PersonId(), ct);
        var tallies = await responses.TalliesAsync(posted.Where(a => a.Content?.IsQuestion == true)
            .Select(a => a.Id).ToArray(), http.PersonId(), ct);
        var reactionTallies = await reactions.TalliesAsync(posted.Select(a => a.Id).ToArray(), http.PersonId(), ct);

        return Results.Ok(new
        {
            announcements = posted.Select(a => new
            {
                id = a.Id,

                // The organizer's words, unchanged. Nothing is truncated or
                // reformatted on the way out: a notice cut off mid-sentence by
                // this layer would be a schedule change nobody could act on.
                body = a.Body,

                // An instant, rendered by the portal. Same rule as the RSVP
                // deadline: the zone is a display decision and this side does
                // not know the reader.
                at = a.PostedAt,
                content = AnnouncementPresentation.ForApplicant(a.Content, tallies.GetValueOrDefault(a.Id)),
                results = AnnouncementPresentation.Results(a.Content, tallies.GetValueOrDefault(a.Id)),
                reactions = reactionTallies.GetValueOrDefault(a.Id) ?? AnnouncementReactions.Empty(),
            }),
        });
    }

    public sealed record AnnouncementVoteRequest(int? Choice);

    public sealed record AnnouncementReactionRequest(string? Reaction);

    private static Task<IResult> ReactToAnnouncement(
        Guid id, AnnouncementReactionRequest? request, HttpContext http,
        PostgresAnnouncementReactionStore reactions, CancellationToken ct)
    {
        if (request?.Reaction is null || !AnnouncementReactions.IsValid(request.Reaction))
            return Task.FromResult<IResult>(Results.BadRequest(new { error = "Choose one of the available reactions." }));
        return SaveAnnouncementReaction(id, request.Reaction, http, reactions, ct);
    }

    private static Task<IResult> RemoveAnnouncementReaction(
        Guid id, HttpContext http, PostgresAnnouncementReactionStore reactions, CancellationToken ct) =>
        SaveAnnouncementReaction(id, null, http, reactions, ct);

    private static async Task<IResult> SaveAnnouncementReaction(
        Guid id, string? reaction, HttpContext http, PostgresAnnouncementReactionStore reactions, CancellationToken ct)
    {
        var result = await reactions.SetAsync(id, http.PersonId(), reaction, ct);
        if (result.Error is not null)
            return Results.Json(new { error = result.Error }, statusCode: result.StatusCode);
        var tallies = await reactions.TalliesAsync([id], http.PersonId(), ct);
        return Results.Ok(new { reactions = tallies.GetValueOrDefault(id) ?? AnnouncementReactions.Empty() });
    }

    private static async Task<IResult> VoteAnnouncement(
        Guid id, AnnouncementVoteRequest? request, HttpContext http,
        PostgresAnnouncementResponseStore responses, CancellationToken ct)
    {
        if (request?.Choice is null)
            return Results.BadRequest(new { error = "Choose an answer first." });
        var result = await responses.VoteAsync(id, http.PersonId(), request.Choice.Value, ct);
        if (result.Error is not null)
            return Results.Json(new { error = result.Error }, statusCode: result.StatusCode);
        var tallies = await responses.TalliesAsync([id], http.PersonId(), ct);
        var tally = tallies.GetValueOrDefault(id);
        return Results.Ok(new
        {
            content = AnnouncementPresentation.ForApplicant(result.Content, tally),
            results = AnnouncementPresentation.Results(result.Content, tally),
        });
    }

    /// <summary>
    /// The code they show at the door, or the sentence explaining why there is
    /// not one yet.
    /// </summary>
    /// <remarks>
    /// Its own route rather than a field on <see cref="Me"/>. That one is read
    /// on every screen in the portal and this one mints a value the first time
    /// it is asked, and a status page that quietly created something would be
    /// a surprising thing to find later.
    /// <para>
    /// A GET that can write, which the sign-out button in this same app
    /// deliberately is not. The difference is that this one is idempotent in
    /// the way that matters: the first call creates the code and every call
    /// after it returns the same twelve characters, so a link prefetcher
    /// cannot cause anything a person would notice. Minting on a POST instead
    /// would mean the screen showing the code needs a button before it can
    /// show it, which is a worse thing to hand somebody in a queue.
    /// </para>
    /// <para>
    /// Answers 200 in every case, including for somebody with no application
    /// and somebody who has not confirmed. There is always a screen, and what
    /// changes is whether it has a code on it — the alternative is a 404 that
    /// the portal would have to translate back into the sentence this route
    /// already knows.
    /// </para>
    /// </remarks>
    private static async Task<IResult> CheckIn(
        HttpContext http, IApplicantPortalStore store, CancellationToken ct)
    {
        var personId = http.PersonId();
        var application = await store.FindForPersonAsync(personId, ct);

        var words = CheckInView.Describe(
            application?.Status, application?.DecisionsAnnounced ?? false);

        // Asked for only when the status is one that has a code, which keeps
        // the common empty case at one query rather than two and, more to the
        // point, stops a code outliving the spot it belongs to. The store
        // returns whatever is stored on the row: the right answer to "what is
        // this person's code" and the wrong one to "what does this screen
        // show", because a code minted while somebody was confirmed stays on
        // the row after they withdraw. Left in, the page printed it directly
        // above its own sentence saying the code appears once a spot is
        // confirmed. The desk re-reads the status on every scan, so what this
        // fixes is a page contradicting itself, not who gets through the door.
        var code = application is not null && CheckInCode.Issued.Contains(application.Status)
            ? await store.CheckInCodeAsync(personId, ct)
            : null;

        return Results.Ok(new
        {
            words.Heading,
            words.Explanation,
            words.Hint,
            code,

            // The grouped spelling comes from the same place as the alphabet.
            // A portal that split the string itself would be deciding where
            // the gaps fall, and the gaps are part of reading it out loud.
            display = code is null ? null : CheckInCode.Format(code),

            // Modules, not an image. The portal decides how large they are
            // drawn and what they are drawn with; this side only knows which
            // ones are dark.
            qr = code is null ? null : QrCode.Encode(code),

            // Said plainly rather than left for the screen to infer from the
            // heading, because the screen has to change shape for it.
            checkedIn = application?.Status is ApplicationStatus.CheckedIn,
        });
    }

    /// <summary>
    /// The only shape an application leaves this API in.
    /// </summary>
    /// <remarks>
    /// One projection used by both handlers, so a field cannot be exposed on
    /// one route and withheld on the other. There is no <c>status</c> key here
    /// on purpose: what the caller gets is the sentence, not the enum.
    /// </remarks>
    private static object Describe(ApplicantApplication application)
    {
        var status = application.Status;
        var announced = application.DecisionsAnnounced;
        var deadline = application.RsvpDeadline;
        var now = DateTimeOffset.UtcNow;

        // Sent only to somebody who has already been told they were accepted.
        // The date itself is a decision: an applicant reading "Application
        // received" who can also see a deadline in the response has been told
        // the answer by a field nobody meant to say it.
        var visibleDeadline = announced && status is ApplicationStatus.Accepted
            ? deadline
            : null;

        return new
        {
            statusLabel = ApplicantView.Describe(
                status, announced, deadline, application.EventStartsAt),
            nextStep = ApplicantView.NextStep(
                status, announced, deadline, application.EventStartsAt),
            // "received", not "submitted". The applicant is told their
            // application was received; naming the field after the internal
            // status is how that word gets onto a screen by accident.
            receivedAt = application.SubmittedAt,

            // The same rule the write is judged by, so the screen cannot offer
            // a button the endpoint would refuse — nor withhold one it would
            // accept, which is the failure nobody notices.
            rsvp = new
            {
                open = Rsvp.IsOpen(status, announced, deadline, now),

                // An instant. The portal renders it in the event's zone, which
                // is a thing the reader's browser knows how to do and this API
                // does not know the reader.
                deadline = visibleDeadline,

                // Null while it is open. Non-null otherwise, including for
                // somebody with nothing to answer — and in that case it is the
                // same sentence for every undecided-looking status, which is
                // what stops it being a decision.
                closedReason = Rsvp.WhyClosed(status, announced, deadline, now),
            },

            // Judged by the same rule the write is, so the screen cannot offer
            // a button the endpoint would refuse. The reason is sent even when
            // it is open — null then — because the panel says one or the other
            // and a screen that had to infer the sentence would be writing a
            // second copy of the wording the team signed off.
            withdraw = new
            {
                open = Withdrawal.IsOpen(status, announced),
                closedReason = Withdrawal.WhyClosed(status, announced),
            },

            profileEditable = ProfileEditing.IsOpen(status),
            profileLockedReason = ProfileEditing.WhyClosed(status),
            profile = new
            {
                firstName = application.Profile.FirstName,
                lastName = application.Profile.LastName,
                school = application.Profile.School,
                shirtSize = application.Profile.ShirtSize,
                dietaryNeeds = application.Profile.DietaryNeeds,
                accessibilityNeeds = application.Profile.AccessibilityNeeds,
            },
            shirtSizes = ShirtSizes.All,
        };
    }

    private const int TextLimit = 500;
    private const int NameLimit = 120;

    private static bool TooLong(string? value, int limit = TextLimit) =>
        value is not null && value.Trim().Length > limit;
}
