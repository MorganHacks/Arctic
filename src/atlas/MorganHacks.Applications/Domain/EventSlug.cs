using System.Text.RegularExpressions;

namespace MorganHacks.Applications.Domain;

/// <summary>
/// The identifier an event is known by.
/// </summary>
/// <remarks>
/// Author-supplied, not derived from the name. Deriving looks tidier and gets
/// the answer wrong: the name is "MorganHacks 2027" and the slug people
/// actually use is <c>mh2027</c>, which no derivation would ever produce. A
/// derived slug also turns a second event with the same name into a silent
/// <c>-2</c> suffix, where an author-supplied one is a refusal naming the slug
/// that is already taken.
/// <para>
/// Unique, and that is enforced by the column rather than by a check here: the
/// index has been on <c>applications.events.slug</c> since 0004 and holds
/// whichever code path did the insert. What this type adds is that the
/// uniqueness means what it looks like — <c>MH2027</c> and <c>mh2027</c> are
/// two rows to that index and one link to a person, so case is normalised
/// before it ever reaches the index.
/// </para>
/// <para>
/// Lowercase letters, digits and single interior hyphens, and nothing else. A
/// slug is pasted into a URL, so a slash would silently become another path
/// segment and a space would arrive percent-encoded and unreadable. The same
/// rule is a NOT VALID check constraint in 0020, because a hand-written INSERT
/// during the event is exactly what skips this file.
/// </para>
/// </remarks>
public static partial class EventSlug
{
    /// <summary>Short enough to type, long enough to say which year.</summary>
    public const int MinimumLength = 2;

    public const int MaximumLength = 40;

    /// <summary>
    /// Normalises what an author typed, or returns null if it is not a slug.
    /// </summary>
    /// <remarks>
    /// Trimming and lowercasing rather than refusing, because a trailing space
    /// and a capital letter are typing rather than intent, and there is exactly
    /// one thing either could have meant. Everything else is refused: a slug
    /// with a slash in it has no single obvious correction, and guessing one
    /// gives somebody an identifier they did not choose.
    /// </remarks>
    public static string? Normalise(string? typed)
    {
        var slug = typed?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(slug)
            || slug.Length < MinimumLength
            || slug.Length > MaximumLength
            || !Shape().IsMatch(slug))
        {
            return null;
        }

        return slug;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Shape();
}
