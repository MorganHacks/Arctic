using System.Text.RegularExpressions;

namespace MorganHacks.Lark.Data.Domain;

/// <summary>
/// What <see cref="EmailHtml.Sanitize"/> is about to take out, said in advance.
/// </summary>
/// <remarks>
/// The allow-list is right and none of this changes it. What was wrong was that
/// it worked in silence: a template written as web HTML saves, previews and
/// sends without a word, and the first anybody hears of it is the email landing
/// looking half-finished. It did. That is what this is for.
/// <para>
/// Only the removals somebody would <i>see</i>. A stripped <c>&lt;script&gt;</c>
/// does not change how the mail looks and saying so would train people to skip
/// the notes, which costs more than it gives.
/// </para>
/// </remarks>
public static partial class EmailHtmlNotes
{
    [GeneratedRegex(@"<style\b", RegexOptions.IgnoreCase)]
    private static partial Regex StyleBlock { get; }

    [GeneratedRegex(@"<link\b[^>]*\bstylesheet\b", RegexOptions.IgnoreCase)]
    private static partial Regex StyleSheetLink { get; }

    [GeneratedRegex(@"<[a-z][^>]*\sclass\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex ClassAttribute { get; }

    /// <summary>
    /// Sentences for the author, or nothing when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Read from the source rather than by diffing the output. The sanitised
    /// body no longer contains a style block whether or not one was written, so
    /// the output cannot answer the question being asked.
    /// </remarks>
    public static IReadOnlyList<string> For(string? format, string? source)
    {
        if (format != TemplateBody.Html || string.IsNullOrWhiteSpace(source))
        {
            return [];
        }

        var notes = new List<string>();
        var blocks = StyleBlock.Count(source);

        if (blocks > 0)
        {
            notes.Add(
                blocks == 1
                    ? "The <style> block was removed, and every rule in it with it. "
                      + "Mail clients ignore or strip stylesheets, so styling has to be "
                      + "written inline: style=\"...\" on each element."
                    : $"All {blocks} <style> blocks were removed, and every rule in them "
                      + "with them. Mail clients ignore or strip stylesheets, so styling "
                      + "has to be written inline: style=\"...\" on each element.");

            // Only worth saying once the stylesheet is gone. On its own a class
            // name is harmless; after the rules have gone it is the reason the
            // layout collapsed, and the thing the author will otherwise stare at
            // wondering why it did nothing.
            if (ClassAttribute.IsMatch(source))
            {
                notes.Add(
                    "The class attributes were kept, but nothing defines them any more. "
                    + "Anything they were positioning, sizing or spacing is now unset.");
            }
        }

        if (StyleSheetLink.IsMatch(source))
        {
            notes.Add(
                "The linked stylesheet was removed. Mail is read offline and inside "
                + "clients that do not fetch it, so it could not have applied anyway.");
        }

        return notes;
    }
}
