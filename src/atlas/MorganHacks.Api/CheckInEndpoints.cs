using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Domain;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// Redeeming a check-in code.
/// </summary>
/// <remarks>
/// One route, and the whole of the door's half of check-in. There is no screen
/// on the other end of it yet: the camera, the volunteer's view and the queue
/// counter are separate work nobody has asked for, and building an endpoint
/// against a screen that does not exist is how an API ends up shaped like a
/// component.
/// <para>
/// Gated on <c>checkin.scan</c>, which logistics and volunteers hold and
/// nobody else does. That permission is the reason this route can take an
/// application id in the form of a code without a further ownership check: a
/// volunteer is allowed to check in anybody, and the thing stopping them
/// checking in the wrong person is that they cannot invent sixty bits.
/// </para>
/// <para>
/// The status change goes through <see cref="IApplicationStore.TransitionAsync"/>
/// and could not usefully go anywhere else. That method is the only writer
/// that tells the transaction who is acting, which is what the trigger reads
/// to fill in <c>checked_in_by</c> and the actor on the history row. An UPDATE
/// written here would still move the status, still write a history row, and
/// leave both saying nobody did it.
/// </para>
/// <para>
/// Nothing here logs a code or a name. The code is a bearer value and a name
/// is somebody's; person ids and an outcome are enough to count a morning.
/// </para>
/// </remarks>
public static class CheckInEndpoints
{
    public static IEndpointRouteBuilder MapCheckIn(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/check-in/scan", Scan)
           .RequirePermission(Permission.CheckinScan);

        return app;
    }

    /// <summary>
    /// The code, in whatever shape the scanner or the keyboard produced it.
    /// </summary>
    /// <remarks>
    /// Nullable and checked in the handler rather than required on the
    /// parameter, like every other body in this codebase: minimal APIs bind
    /// before endpoint filters run, so a required body answers a request with
    /// no session by describing the route instead of refusing it.
    /// </remarks>
    public sealed record ScanRequest(string? Code);

    /// <summary>
    /// Checks somebody in. Requires <c>checkin.scan</c>.
    /// </summary>
    /// <remarks>
    /// Four answers, and the split between them is about what a volunteer does
    /// next rather than about what happened in the database.
    /// <list type="bullet">
    /// <item>
    /// <c>checkedIn</c>, 200. They were confirmed and they are now counted.
    /// </item>
    /// <item>
    /// <c>alreadyCheckedIn</c>, also 200, and this is the decision in the file
    /// worth defending. Two volunteers scanning the same person is what a
    /// queue does, not a fault: the second scanner asked whether this person
    /// may come in, and the answer is still yes. Answering 409 would paint a
    /// normal event red, and a desk that sees red on ordinary traffic stops
    /// reading it by the fortieth person. The response says which of the two
    /// it was, and carries the time of the original check-in so a screen can
    /// show "already in, two minutes ago" rather than pretending it just
    /// happened. Nothing is written the second time, so the trail keeps
    /// naming the volunteer who actually arrived at them first.
    /// </item>
    /// <item>
    /// <c>notConfirmed</c>, 409. There is an application and the lifecycle
    /// will not move it to checked in. Only <c>confirmed</c> may, which is
    /// <see cref="StatusTransition"/>'s rule and not restated here.
    /// </item>
    /// <item>
    /// <c>unknownCode</c>, 404. Nothing carries that code.
    /// </item>
    /// </list>
    /// <para>
    /// Every one of them carries a sentence naming an action, because the
    /// person reading it is standing in a doorway with a queue behind them.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Scan(
        ScanRequest? request,
        HttpContext http,
        ICheckInStore codes,
        IApplicationStore applications,
        ILogger<ScanRequest> log,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Code))
        {
            return Results.BadRequest(new { error = "Scan or type a code." });
        }

        // A code that will not normalise is refused with the same sentence as
        // one that simply matches nothing. To a volunteer they are the same
        // event -- what they are holding is not a code we issued -- and
        // splitting them would only tell somebody guessing which of their
        // guesses were the right shape.
        if (!CheckInCode.TryNormalise(request.Code, out var code))
        {
            return Unknown(http, log);
        }

        var subject = await codes.FindByCodeAsync(code, ct);
        if (subject is null)
        {
            return Unknown(http, log);
        }

        if (subject.Status is ApplicationStatus.CheckedIn)
        {
            return Admitted(http, log, ScanOutcome.AlreadyCheckedIn, subject, subject.CheckedInAt);
        }

        if (!StatusTransition.IsAllowed(subject.Status, ApplicationStatus.CheckedIn))
        {
            log.LogInformation(
                "A check-in scan was refused. {actor} {applicationId} {outcome} {event}",
                http.PersonId(), subject.ApplicationId, ScanOutcome.NotConfirmed,
                Events.CheckInScanned);

            return Results.Conflict(new
            {
                outcome = ScanOutcome.NotConfirmed,
                error = CheckInDesk.WhyNot(subject.Status),
                name = Name(subject),
            });
        }

        try
        {
            var change = await applications.TransitionAsync(
                subject.ApplicationId,
                ApplicationStatus.CheckedIn,
                actorId: http.PersonId(),
                reason: ScanReason,
                ct: ct);

            return Admitted(http, log, ScanOutcome.CheckedIn, subject, change.At);
        }
        catch (InvalidTransitionException e) when (e.From is ApplicationStatus.CheckedIn)
        {
            // Two volunteers reached the same person inside the same second.
            // The row lock in TransitionAsync means one of them won and this
            // one re-read 'checked_in', so the honest answer is the one the
            // other scanner already got. Re-reading rather than reporting now:
            // the time that belongs on the screen is when they were let in,
            // not when the second phone finished asking.
            var settled = await codes.FindByCodeAsync(code, ct);

            return Admitted(
                http, log, ScanOutcome.AlreadyCheckedIn, subject, settled?.CheckedInAt);
        }
    }

    /// <summary>
    /// What lands in the history row's reason column.
    /// </summary>
    /// <remarks>
    /// Fixed rather than taken from the request. The trail already names the
    /// volunteer and the time; what it cannot otherwise say is whether
    /// somebody was scanned at the door or moved by hand from the console
    /// afterwards, and those two are different facts about the same person.
    /// </remarks>
    private const string ScanReason = "Check-in code scanned.";

    private static IResult Unknown(HttpContext http, ILogger log)
    {
        // No application id, because there is no application. The line still
        // goes out: a burst of these is a scanner misreading, which is the
        // thing worth catching while the queue is still there.
        log.LogInformation(
            "A check-in scan matched no application. {actor} {outcome} {event}",
            http.PersonId(), ScanOutcome.UnknownCode, Events.CheckInScanned);

        return Results.NotFound(new
        {
            outcome = ScanOutcome.UnknownCode,
            error = CheckInDesk.Describe(ScanOutcome.UnknownCode),
        });
    }

    private static IResult Admitted(
        HttpContext http,
        ILogger log,
        ScanOutcome outcome,
        CheckInSubject subject,
        DateTimeOffset? at)
    {
        log.LogInformation(
            "A check-in scan was accepted. {actor} {applicationId} {outcome} {event}",
            http.PersonId(), subject.ApplicationId, outcome, Events.CheckInScanned);

        return Results.Ok(new
        {
            outcome,
            message = CheckInDesk.Describe(outcome),

            // The one field that makes a forwarded code useless. The volunteer
            // reads this while looking at whoever handed them the phone.
            name = Name(subject),
            alreadyCheckedIn = outcome is ScanOutcome.AlreadyCheckedIn,
            checkedInAt = at,
        });
    }

    /// <summary>
    /// The name to show, or null when the row somehow has none.
    /// </summary>
    /// <remarks>
    /// Null cannot happen for anybody who reached <c>confirmed</c> -- the
    /// completeness constraint on the table requires both halves from the
    /// moment an application stops being a draft. Handled anyway, because a
    /// null reference on the check-in desk at seven in the morning is a worse
    /// outcome than a screen that says less than it meant to.
    /// </remarks>
    private static string? Name(CheckInSubject subject)
    {
        var name = $"{subject.FirstName} {subject.LastName}".Trim();
        return name.Length == 0 ? null : name;
    }
}
