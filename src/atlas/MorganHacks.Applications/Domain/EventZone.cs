namespace MorganHacks.Applications.Domain;

/// <summary>
/// The zone every date said to an applicant is said in.
/// </summary>
/// <remarks>
/// An <c>rsvp_deadline</c> is an instant, and nobody thinks in instants.
/// Somebody setting "confirm by January 15th at 11:59pm" means an evening in
/// the event's city; that same instant written in UTC is the sixteenth at five
/// in the morning. Formatted without a zone, a deadline the team set on the
/// fifteenth is shown to the applicant as the sixteenth — a day out, for
/// exactly the people the date was for.
/// <para>
/// The event's zone rather than the reader's, matching what the console and
/// the public form already do. A deadline that agrees with the flyer is one
/// the two can be checked against each other, and a fixed zone renders the
/// same on the server as it does on a phone in Denver.
/// </para>
/// <para>
/// Named through <see cref="TimeZoneInfo"/> rather than a fixed offset, so the
/// standard/daylight switch is handled rather than assumed. The project has
/// been caught by that before: the 2026 deadline was written up as EST in a
/// month that was on EDT.
/// </para>
/// </remarks>
public static class EventZone
{
    /// <summary>The IANA id. Matches the two copies in the console.</summary>
    public const string Id = "America/New_York";

    /// <summary>
    /// The zone, or UTC on a host that has never heard of it.
    /// </summary>
    /// <remarks>
    /// Falling back rather than throwing, because the throw would be at static
    /// initialisation on a slim image with no tzdata — which takes out every
    /// route in the API, including the ones that say no date at all. A date an
    /// hour or five out is a bad screen; a portal that will not start is a
    /// worse one, and the difference is visible in the abbreviation either way.
    /// </remarks>
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.TryFindSystemTimeZoneById(Id, out var found) ? found : TimeZoneInfo.Utc;

    /// <summary>The same instant, read off the clock in the event's city.</summary>
    public static DateTimeOffset Local(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Zone);
}
