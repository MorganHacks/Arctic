using System.Text.Json;
using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Forms;

/// <summary>
/// Somebody we already have on file, as a sign-in form needs them.
/// </summary>
/// <remarks>
/// Built from their application for the event the form belongs to, which is
/// the only record a form can join an answer to. Somebody signed in with no
/// application for this event is not a respondent at all — there is nothing to
/// prefill from and no status to check an audience against — so this type is
/// null in that case rather than half-populated.
/// </remarks>
/// <param name="Status">
/// The stored spelling, checked against <see cref="Form.EligibleStatuses"/>.
/// It must not leave the API: the portal is careful never to show an applicant
/// their internal status, and a form that leaked one would undo that for the
/// same person on a different page.
/// </param>
/// <param name="Known">
/// What they have already told us, keyed the way a question would be. The
/// prefill is drawn from here and nowhere else.
/// </param>
public sealed record Respondent(
    Guid PersonId,
    Guid? ApplicationId,
    string Email,
    string? FirstName,
    string? LastName,
    string Status,
    bool AgreedToCodeOfConduct,
    bool AgreedToDataSharing,
    IReadOnlyDictionary<string, JsonElement> Known)
{
    /// <summary>Their name as a form should print it, or null.</summary>
    /// <remarks>
    /// Null rather than a blank string when we hold neither half, so the page
    /// can fall back to the address instead of rendering an empty line where a
    /// name should be.
    /// </remarks>
    public string? Name =>
        string.Join(' ', new[] { FirstName, LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))) is { Length: > 0 } name
            ? name
            : null;
}

/// <summary>
/// The questions a respondent may not answer for themselves.
/// </summary>
/// <remarks>
/// A sign-in form prefills what somebody already told us and lets them correct
/// it, because the whole reason for signing in is that we should not be making
/// them retype what we hold. Two groups are the exception, and both are
/// exceptions for the same reason: the value is not a preference, it is a
/// record of something that already happened.
/// <list type="number">
/// <item>
/// <b>Identity.</b> The address is what the magic link was sent to and
/// therefore the one fact this session actually proves. Letting it be edited
/// on the form would detach the answer from the application it exists to be
/// joined to — which is the exact failure sign-in forms were built to remove,
/// reintroduced one field lower down. The name goes with it: it is what the
/// badge, the check-in list and the certificate are printed from, and a name
/// changed on an RSVP would disagree with all three. Changing either is a
/// support request, not a form field, because a person changing their own
/// identifying details is a thing somebody should see happen.
/// </item>
/// <item>
/// <b>Agreements already given.</b> MLH's code of conduct and data sharing are
/// stored as the moment they were agreed, against the form version that was
/// shown. Re-ticking a box they already ticked would restate an agreement with
/// today's date and lose the real one; leaving it unticked would read as
/// withdrawing consent we are obliged to hold. Neither is what an RSVP is for,
/// and a survey is not the place to renegotiate a legal agreement.
/// </item>
/// </list>
/// <para>
/// Fixed rather than hidden, on purpose. A question that vanishes is one
/// somebody assumes was never asked; a question shown with its answer and no
/// way to change it says what we hold and that they are not the one to change
/// it. Hiding it would also mean an author who put the question on the form
/// gets a form that silently does not ask it.
/// </para>
/// <para>
/// The list is a property of the respondent rather than a constant, because an
/// agreement is only fixed once it has been given. Somebody eligible on an
/// <c>incomplete</c> application may never have agreed to anything, and
/// freezing an empty agreement would leave them unable to give one.
/// </para>
/// </remarks>
public static class FixedAnswers
{
    /// <summary>Verified at sign-in, and not theirs to change here.</summary>
    private static readonly string[] Identity =
        [AnswerColumns.Email, "first_name", "last_name"];

    /// <summary>The two agreements stored as the moment they were given.</summary>
    private static readonly string[] Agreements =
        ["mlh_coc_agreed_at", "mlh_data_sharing_at"];

    /// <summary>Which keys this person may not set on this form.</summary>
    public static IReadOnlySet<string> For(Respondent respondent)
    {
        var fixedKeys = new HashSet<string>(Identity, StringComparer.Ordinal);

        if (respondent.AgreedToCodeOfConduct)
        {
            fixedKeys.Add(Agreements[0]);
        }

        if (respondent.AgreedToDataSharing)
        {
            fixedKeys.Add(Agreements[1]);
        }

        return fixedKeys;
    }

    /// <summary>
    /// The answers as they will actually be stored, whatever arrived.
    /// </summary>
    /// <remarks>
    /// The half of this rule a browser cannot be trusted with. The page renders
    /// a fixed question as text with no control, and that is worth doing — but
    /// a page is a suggestion, and anybody can post whatever dictionary they
    /// like to the submit endpoint.
    /// <para>
    /// Overwritten rather than refused. A crafted submission and a stale tab
    /// produce the same request, so a 400 would fail an honest applicant to
    /// scold a dishonest one; replacing the value means the crafted answer
    /// simply never existed. It also has to happen <em>before</em> validation
    /// rather than after, or a required fixed question posted empty would be
    /// refused for being unanswered when we are holding the answer.
    /// </para>
    /// </remarks>
    public static Dictionary<string, JsonElement> Apply(
        IReadOnlyDictionary<string, JsonElement> submitted,
        IReadOnlyList<FormField> fields,
        Respondent respondent)
    {
        var answers = new Dictionary<string, JsonElement>(submitted, StringComparer.Ordinal);
        var locked = For(respondent);

        foreach (var field in fields)
        {
            if (!locked.Contains(field.Key))
            {
                continue;
            }

            if (respondent.Known.TryGetValue(field.Key, out var held))
            {
                answers[field.Key] = held;
            }
            else
            {
                // Nothing on file for a key we will not accept from them
                // either. Removed rather than left as whatever arrived, so the
                // question reads as unanswered — which is what it is.
                answers.Remove(field.Key);
            }
        }

        return answers;
    }
}

/// <summary>Whether a status may open a form, spelled for a reader.</summary>
public static class EligibleStatuses
{
    /// <summary>
    /// The eleven the schema allows, in lifecycle order.
    /// </summary>
    /// <remarks>
    /// Offered to the builder so an author picks from the real set rather than
    /// typing one. Derived from the enum so a status added there cannot be
    /// missing here, and ordered by the enum's own declaration order because
    /// that is the order somebody moves through them.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } =
        [.. Enum.GetValues<ApplicationStatus>().Select(status => status.ToWire())];

    /// <summary>Whether every one of these is a status we recognise.</summary>
    public static bool AllKnown(IEnumerable<string> statuses) =>
        statuses.All(status => All.Contains(status, StringComparer.Ordinal));
}
