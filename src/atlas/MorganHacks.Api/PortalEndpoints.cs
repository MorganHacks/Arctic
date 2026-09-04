using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

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
/// Nothing here logs an address, a name or an answer. Person ids only, like
/// the rest of the codebase — see <c>Redaction.SensitiveKeys</c> for the net
/// underneath that rule.
/// </para>
/// </remarks>
public static class PortalEndpoints
{
    public static IEndpointRouteBuilder MapPortal(this IEndpointRouteBuilder app)
    {
        var portal = app.MapGroup("/portal").RequireSession();

        portal.MapGet("/me", Me);
        portal.MapPatch("/profile", SaveProfile);
        portal.MapGet("/messages", Messages);
        portal.MapGet("/check-in", CheckIn);

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

        // Not asked for at all when there is no application, so the common
        // empty case costs one query rather than two.
        var code = application is null ? null : await store.CheckInCodeAsync(personId, ct);

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

        return new
        {
            statusLabel = ApplicantView.Describe(
                status, announced, application.RsvpDeadline, application.EventStartsAt),
            nextStep = ApplicantView.NextStep(
                status, announced, application.RsvpDeadline, application.EventStartsAt),
            // "received", not "submitted". The applicant is told their
            // application was received; naming the field after the internal
            // status is how that word gets onto a screen by accident.
            receivedAt = application.SubmittedAt,
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
