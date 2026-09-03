using System.Globalization;
using System.Text.Json;
using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Segments;

/// <summary>
/// Who a broadcast is aimed at.
/// </summary>
/// <remarks>
/// Three shapes, deliberately, and no fourth. Every one of them answers a
/// question the registration team actually asks — "tell everyone we accepted",
/// "tell everyone who filled in the mentor form", "tell these four people" —
/// and none of them is a query builder.
/// <para>
/// A query builder is the obvious next step and it is the wrong one. It turns
/// a stored segment into a stored program, so reading it back a month later
/// means re-implementing the evaluator to know what it meant; it makes every
/// column of <c>applications.*</c> part of the API, so the schema can no
/// longer change; and it hands somebody the ability to compose a filter nobody
/// reviewed into several hundred emails that cannot be recalled. Three named
/// shapes can each be read as a sentence, which is the property that matters
/// when the question is "who exactly did we email".
/// </para>
/// <para>
/// Parsed from JSON rather than bound by the framework because this is what
/// gets stored, verbatim, in <c>notify.campaigns.segment</c>. The stored
/// document has to survive being read by something that is not this class, so
/// it is a plain tagged object rather than whatever a serializer happens to
/// emit for a hierarchy.
/// </para>
/// </remarks>
public abstract record Segment
{
    /// <summary>The discriminator, as it is written in the stored document.</summary>
    public abstract string Type { get; }

    /// <summary>
    /// Everyone whose application on one event is in one of these states.
    /// </summary>
    /// <remarks>
    /// The decision email, the RSVP reminder, the "you are on the waitlist"
    /// note. Several statuses rather than one because the useful segments are
    /// unions — accepted and confirmed together are "people who are coming or
    /// might be" — and asking somebody to send the same announcement three
    /// times is asking for it to go out twice to somebody who moved between
    /// two of them.
    /// </remarks>
    public sealed record InStatus(Guid EventId, IReadOnlyList<ApplicationStatus> Statuses) : Segment
    {
        public override string Type => "applicationStatus";
    }

    /// <summary>
    /// Everyone who submitted a given form.
    /// </summary>
    /// <remarks>
    /// Named by form rather than by event, because "everyone who answered the
    /// mentor sign-up" is the thing somebody means and it is not the same set
    /// as everyone on the event.
    /// <para>
    /// Only an application form has respondents today. A survey's answers are
    /// refused at submit — <c>PublicFormEndpoints</c> answers 501 rather than
    /// accepting and dropping them — so there is genuinely nobody to resolve,
    /// and this resolves to nothing rather than quietly returning the
    /// applications sitting on the same event.
    /// </para>
    /// </remarks>
    public sealed record FormRespondents(Guid FormId) : Segment
    {
        public override string Type => "formRespondents";
    }

    /// <summary>
    /// These addresses and no others.
    /// </summary>
    /// <remarks>
    /// The escape hatch, and the reason the other two do not need to grow. A
    /// mentor, four sponsors and somebody's supervisor are not a filter over
    /// <c>applications.*</c> and never will be.
    /// <para>
    /// These resolve with no person id, because an address here is frequently
    /// somebody who has no row in this system at all. That is exactly why
    /// 0015 added a unique index on (campaign_id, to_email): the person-based
    /// one from 0003 does not stop a duplicate when the person is unknown.
    /// </para>
    /// </remarks>
    public sealed record Addresses(IReadOnlyList<string> Emails) : Segment
    {
        public override string Type => "explicitList";
    }

    /// <summary>
    /// The most a segment may resolve to.
    /// </summary>
    /// <remarks>
    /// Not a rate limit — lark paces the actual sending at roughly fourteen a
    /// second and drains ten thousand rows in under half an hour, so the queue
    /// is not the thing at risk. This is a sanity bound on a number nobody
    /// intended. This event has several hundred applicants; a segment that
    /// resolves to five figures is a mistake somebody is about to make
    /// irreversibly, and the cheapest place to catch it is before the rows
    /// exist.
    /// </remarks>
    public const int MaxRecipients = 10_000;

    /// <summary>
    /// The document that gets stored.
    /// </summary>
    /// <remarks>
    /// Written from the parsed segment rather than from the JSON that arrived,
    /// so what is on the row is what the server understood — not what somebody
    /// sent plus whatever extra properties rode along unread. A stored segment
    /// that includes a field nothing acts on is a stored segment that lies to
    /// the next person who reads it.
    /// <para>
    /// Statuses go in as their stored spellings rather than enum names, so the
    /// document can be read against <c>applications.applications.status</c>
    /// with no lookup table and survives a C# member being renamed.
    /// </para>
    /// </remarks>
    public string ToJson() => this switch
    {
        InStatus s => JsonSerializer.Serialize(new
        {
            type = s.Type,
            eventId = s.EventId,
            statuses = s.Statuses.Select(x => x.ToWire()),
        }),
        FormRespondents s => JsonSerializer.Serialize(new { type = s.Type, formId = s.FormId }),
        Addresses s => JsonSerializer.Serialize(new { type = s.Type, emails = s.Emails }),
        _ => throw new InvalidOperationException($"No stored shape for '{Type}'."),
    };

    /// <summary>
    /// Reads a segment out of the request, or says what is wrong with it.
    /// </summary>
    /// <remarks>
    /// Every failure returns a sentence somebody can act on. The alternative
    /// is a deserialization exception surfacing as a 400 with a type name in
    /// it, on the screen where somebody is about to mail four hundred people
    /// — the one screen where being told what is wrong actually matters.
    /// </remarks>
    public static bool TryParse(JsonElement json, out Segment? segment, out string? error)
    {
        segment = null;
        error = null;

        if (json.ValueKind != JsonValueKind.Object)
        {
            error = "A segment is an object saying who to send to.";
            return false;
        }

        if (!json.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            error = "A segment needs a type: applicationStatus, formRespondents "
                    + "or explicitList.";
            return false;
        }

        switch (type.GetString())
        {
            case "applicationStatus":
                return TryStatuses(json, out segment, out error);
            case "formRespondents":
                if (!TryId(json, "formId", out var formId))
                {
                    error = "This segment needs the form its recipients answered.";
                    return false;
                }

                segment = new FormRespondents(formId);
                return true;
            case "explicitList":
                return TryAddresses(json, out segment, out error);
            default:
                error = "That is not a segment we know how to send to. "
                        + "Pick applicationStatus, formRespondents or explicitList.";
                return false;
        }
    }

    private static bool TryStatuses(JsonElement json, out Segment? segment, out string? error)
    {
        segment = null;
        error = null;

        if (!TryId(json, "eventId", out var eventId))
        {
            error = "This segment needs the event whose applicants it means.";
            return false;
        }

        if (!json.TryGetProperty("statuses", out var listed)
            || listed.ValueKind != JsonValueKind.Array
            || listed.GetArrayLength() == 0)
        {
            error = "Choose at least one application status to send to.";
            return false;
        }

        var statuses = new List<ApplicationStatus>();
        foreach (var element in listed.EnumerateArray())
        {
            ApplicationStatus parsed;
            try
            {
                parsed = ApplicationStatuses.Parse(element.GetString() ?? string.Empty);
            }
            catch (ArgumentException)
            {
                // Named back, because the list came off a set of checkboxes and
                // the one that is wrong is the only useful thing to say.
                error = string.Format(
                    CultureInfo.InvariantCulture,
                    "'{0}' is not an application status.",
                    element.ValueKind == JsonValueKind.String ? element.GetString() : "that");
                return false;
            }

            if (!statuses.Contains(parsed))
            {
                statuses.Add(parsed);
            }
        }

        segment = new InStatus(eventId, statuses);
        return true;
    }

    private static bool TryAddresses(JsonElement json, out Segment? segment, out string? error)
    {
        segment = null;
        error = null;

        if (!json.TryGetProperty("emails", out var listed)
            || listed.ValueKind != JsonValueKind.Array
            || listed.GetArrayLength() == 0)
        {
            error = "Add at least one address to send to.";
            return false;
        }

        if (listed.GetArrayLength() > MaxRecipients)
        {
            error = string.Format(
                CultureInfo.InvariantCulture,
                "That is more than {0:N0} addresses, which is more than this is for.",
                MaxRecipients);
            return false;
        }

        // Deduplicated as it is read, and case-insensitively, because these
        // are pasted out of a spreadsheet. Two spellings of one address in the
        // list would be caught by the unique index at send anyway; catching it
        // here is what makes the number on the preview screen the truth.
        var emails = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in listed.EnumerateArray())
        {
            var email = element.ValueKind == JsonValueKind.String
                ? element.GetString()?.Trim()
                : null;

            if (string.IsNullOrEmpty(email))
            {
                error = "One of those addresses is blank.";
                return false;
            }

            if (seen.Add(email))
            {
                emails.Add(email);
            }
        }

        segment = new Addresses(emails);
        return true;
    }

    private static bool TryId(JsonElement json, string name, out Guid id)
    {
        id = Guid.Empty;
        return json.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
               && Guid.TryParse(value.GetString(), out id);
    }
}
