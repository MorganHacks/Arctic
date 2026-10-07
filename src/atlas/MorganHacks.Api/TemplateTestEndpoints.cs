using Microsoft.Extensions.Caching.Memory;
using MorganHacks.Identity.Services;
using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api;

public static partial class TemplateEndpoints
{
    public sealed record TemplateTestRequest(TemplateRequest? Draft, string? Recipient, Guid RequestId);

    /// <summary>How many test sends one organizer may queue in an hour.</summary>
    private const int MaxTestsPerHour = 30;

    private static readonly TimeSpan TestWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether this organizer has already queued as many test sends as they are
    /// allowed.
    /// </summary>
    /// <remarks>
    /// The same shape as <see cref="AuthEndpoints.TooManyFor"/> and
    /// <see cref="PublicFormEndpoints.TooManyAnonymous"/> — a counter in an
    /// <see cref="IMemoryCache"/> entry that expires on its own, checked
    /// before the queue is written — and deliberately a third copy rather than
    /// a shared helper. Those two count attempts against an address over
    /// fifteen minutes and rows against a form over an hour; folding this one
    /// in would mean one limit and one window serving two different hazards.
    /// <para>
    /// An <see cref="IMemoryCache"/> counter rather than a rate limiter policy
    /// like the ones in <c>Program</c>, and the reason is ordering: those run
    /// as middleware, which is before the endpoint filter that resolves the
    /// session and puts a person in <c>HttpContext.Items</c>. A policy
    /// partitioned on the actor would find nothing there. The policies that
    /// exist all partition on a client address, which is available that early.
    /// <para>
    /// Partitioned on the organizer rather than on the address, which is the
    /// whole point. Every per-address limiter in this file is loose by
    /// necessity because a campus NAT is a lecture theatre's worth of people
    /// behind one IP; none of that applies here, because the caller is an
    /// authenticated organizer and the count is theirs alone.
    /// <para>
    /// In memory, so with several replicas the real limit is roughly this times
    /// the replica count — the trade both of the other two write down, for the
    /// same reason: a shared counter means Redis, and Redis means another thing
    /// to have fall over during an event. It is a ceiling on an organizer's own
    /// account rather than a security boundary, so the approximation is fine.
    /// </para>
    /// </remarks>
    private static bool TooManyTests(IMemoryCache cache, Guid personId)
    {
        var counter = cache.GetOrCreate($"template-test:{personId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TestWindow;
            return new TestCounter();
        })!;

        lock (counter)
        {
            if (counter.Count >= MaxTestsPerHour)
            {
                return true;
            }

            counter.Count++;
            return false;
        }
    }

    private sealed class TestCounter
    {
        public int Count;
    }

    /// <summary>
    /// Queues a rendered template to the organizer's own inbox.
    /// </summary>
    /// <remarks>
    /// A test send goes to the address on the organizer's own row, and to no
    /// other. That restriction is the security control, not a convenience, and
    /// it is worth saying why it is written this way rather than as a rate
    /// limit.
    /// <para>
    /// This endpoint renders arbitrary HTML through our verified sending
    /// identity. What it used to accept was any address at all, so one
    /// compromised organizer account was a mail cannon pointed at arbitrary
    /// recipients, held together only by two permissions and a reputation we
    /// share with the sign-in links people need to get into the portal. A rate
    /// limit does not close that: it caps the rate and leaves the primitive,
    /// and sending reputation is a function of complaint rate, not of volume —
    /// a few hundred messages are enough. Refusing the other recipients removes
    /// the primitive instead, and collapses the blast radius to "can email
    /// themselves", which is not a security event at all.
    /// <para>
    /// Nothing is lost by it. Both send paths already prefill the dialog with
    /// the organizer's own address — see <c>test-email-dialog.tsx</c> and
    /// <c>api-workspace.tsx</c>, which take <c>defaultRecipient</c> from
    /// <c>person.email</c> — and <c>identity.people.email</c> is NOT NULL, so
    /// there is no organizer for whom this leaves no address to test with. The
    /// one legitimate case it does break is testing on a second inbox, and the
    /// right way to keep that is a separate permission somebody grants on
    /// purpose, not an allowance everybody already has.
    /// <para>
    /// The limit above is defence in depth and bounds the volume of sends a
    /// compromised account can queue against itself. It is not what makes this
    /// endpoint safe, and tightening it should not be mistaken for the reason
    /// the endpoint is safe.
    /// </para>
    /// </remarks>
    private static async Task<IResult> SendTest(TemplateTestRequest? request, HttpContext http,
        TemplateTestQueue tests, MessageQueue queue, IIdentityStore people, IMemoryCache cache,
        ILogger<TemplateTestRequest> log, CancellationToken ct)
    {
        var actor = http.PersonId();
        var recipient = request?.Recipient?.Trim();
        if (string.IsNullOrEmpty(recipient) || !IsAddress(recipient))
            return Results.BadRequest(new { error = "Enter a valid recipient email address." });
        if (request!.RequestId == Guid.Empty)
            return Results.BadRequest(new { error = "Reopen the test email dialog and try again." });

        // Case-insensitively, because the address in the database and the one
        // typed into the dialog are the same address written by two different
        // people and a test refused over the casing of a local part is a bug
        // report, not a control.
        //
        // A null own-address refuses rather than passes. The gate resolved a
        // live session, so the row exists and this should be unreachable — but
        // the unreachable branch of a check that decides who may receive mail
        // is the one worth getting right, and "we could not find out, so allow"
        // is the wrong default for it.
        var own = await people.FindOwnEmailAsync(actor, ct);
        if (own is null || !string.Equals(recipient, own.Trim(), StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "A test email only goes to your own address. Save the template and send the real campaign to test anybody else." });

        if (!TryDraft(request.Draft, "test", out var draft, out var refusal))
            return Results.BadRequest(new { error = refusal });
        if (await queue.IsSuppressedAsync(recipient, draft!.Kind == "transactional", ct))
            return Results.BadRequest(new { error = "This address cannot receive email. Use another test address." });
        if (TooManyTests(cache, actor))
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many test emails",
                detail: $"You have queued {MaxTestsPerHour} test emails in the last hour. Try again later.");
        var id = await tests.EnqueueAsync(draft, recipient, actor, request.RequestId, ct);
        if (id is null) return Results.Conflict(new { error = "Reopen the test email dialog and try again." });
        log.LogInformation("A test email was queued. {actor} {message}", actor, id);
        return Results.Accepted(value: new { id, status = "queued" });
    }
}