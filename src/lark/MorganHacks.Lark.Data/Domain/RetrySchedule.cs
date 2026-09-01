namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// When a temporarily-failed message should next be tried.
/// </summary>
public static class RetrySchedule
{
    /// <summary>After this many attempts a message is given up on.</summary>
    public const int MaxAttempts = 5;

    private static readonly TimeSpan[] Ceilings =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6),
    ];

    /// <summary>
    /// The delay before attempt <paramref name="attempt"/>, or null when the
    /// message has run out of attempts.
    /// </summary>
    /// <remarks>
    /// Full jitter: the delay is a random point between zero and the ceiling,
    /// not the ceiling itself. Without it a thousand messages failing on the
    /// same throttle all retry in the same second and throttle again, and the
    /// backoff achieves nothing except moving the pile-up later.
    /// </remarks>
    public static TimeSpan? DelayFor(int attempt, Random? random = null)
    {
        if (attempt < 1 || attempt > MaxAttempts)
        {
            return null;
        }

        var ceiling = Ceilings[attempt - 1];
        var rng = random ?? Random.Shared;
        return TimeSpan.FromMilliseconds(rng.NextDouble() * ceiling.TotalMilliseconds);
    }
}
