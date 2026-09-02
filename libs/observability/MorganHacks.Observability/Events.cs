namespace MorganHacks.Observability;

/// <summary>
/// Business signals, named once so an alert can be built on them.
/// </summary>
/// <remarks>
/// The failures worth alerting on here are absences, not spikes. If
/// <c>magic_link.requested</c> stays healthy while <c>magic_link.consumed</c>
/// collapses, mail is not arriving: every service is up, every dashboard is
/// green, and nobody can log in. No error rate catches that, because nothing
/// is erroring.
/// <para>
/// Emitted as a property on a log line rather than through a metrics library.
/// A counter needs somewhere to go, and one more thing to run is a worse trade
/// than a field an aggregator can already count.
/// </para>
/// </remarks>
public static class Events
{
    /// <summary>The property these appear under.</summary>
    public const string Property = "event";

    /// <summary>A sign-in link was genuinely queued for somebody.</summary>
    public const string MagicLinkRequested = "magic_link.requested";

    /// <summary>Somebody clicked one and got a session.</summary>
    public const string MagicLinkConsumed = "magic_link.consumed";

    /// <summary>A message was accepted by the provider.</summary>
    public const string MessageSent = "message.sent";

    /// <summary>An address was added to the suppression list.</summary>
    public const string AddressSuppressed = "address.suppressed";

    /// <summary>
    /// Somebody changed somebody else's access.
    /// </summary>
    /// <remarks>
    /// The permission model requires that every grant change be attributable:
    /// "who gave this person export at 2am" must have an answer. Until there
    /// is an audit table these lines are that answer, which is why they carry
    /// both person ids and never the address — <c>actor</c> did it,
    /// <c>subject</c> had it done to them.
    /// </remarks>
    public const string OrganizerAdded = "access.organizer_added";

    /// <summary>A team membership was added, retimed, or removed.</summary>
    public const string TeamChanged = "access.team_changed";

    /// <summary>An individual grant was added, retimed, or removed.</summary>
    public const string GrantChanged = "access.grant_changed";

    /// <summary>Somebody was taken off the allowlist and their sessions cut.</summary>
    public const string PersonRevoked = "access.person_revoked";

    /// <summary>
    /// Somebody finished the application form.
    /// </summary>
    /// <remarks>
    /// The absence to watch during registration week. The form being served
    /// and the form being completed are separate things, and a bad question,
    /// a broken validation rule, or a submit button that silently 500s all
    /// look identical from the outside: traffic arrives, nothing errors, and
    /// the count of applications stops climbing.
    /// </remarks>
    public const string ApplicationSubmitted = "application.submitted";
}
