namespace MorganHacks.Applications.Domain;

/// <summary>
/// The part of an application the applicant owns after submitting.
/// </summary>
/// <remarks>
/// Deliberately six fields and not the whole row. Everything an application is
/// judged on — the essays, the school year, the agreements — is fixed at
/// submit, because letting somebody rewrite what a reviewer already read is a
/// different feature with a different audit story. What is left is the
/// logistics: how to spell their name on a badge, what shirt to order, what
/// they can eat, and what they need to take part.
/// </remarks>
public sealed record ApplicantProfile
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? School { get; init; }
    public string? ShirtSize { get; init; }

    /// <summary>Free text, because the useful answers are never in a list.</summary>
    public string? DietaryNeeds { get; init; }

    public string? AccessibilityNeeds { get; init; }
}

/// <summary>
/// The shirt sizes we actually order.
/// </summary>
/// <remarks>
/// A closed set because this number goes to a printer. Free text produces
/// "M", "m", "medium" and "Mens Medium" in the same column, and somebody
/// reconciles that by hand at midnight the week of the event.
/// </remarks>
public static class ShirtSizes
{
    public static readonly IReadOnlyList<string> All =
        ["xs", "s", "m", "l", "xl", "2xl", "3xl"];

    private static readonly HashSet<string> Known =
        new(All, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalises a submitted size, or reports that we do not order it.
    /// </summary>
    /// <remarks>
    /// Blank is valid and means unanswered — a shirt size is not something we
    /// should stop somebody saving their dietary needs over.
    /// </remarks>
    public static bool TryNormalise(string? submitted, out string? size)
    {
        var trimmed = submitted?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            size = null;
            return true;
        }

        if (!Known.Contains(trimmed))
        {
            size = null;
            return false;
        }

        size = trimmed.ToLowerInvariant();
        return true;
    }
}
