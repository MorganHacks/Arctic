namespace MorganHacks.Applications.Segments;

/// <summary>What a mergeable column holds, and therefore how it reads.</summary>
/// <remarks>
/// Named after the Postgres type rather than after the rendering, because this
/// is checked against <c>information_schema.columns</c> by a test. A kind that
/// said <c>YesNo</c> could not be compared to anything.
/// </remarks>
public enum ColumnKind
{
    /// <summary><c>text</c>.</summary>
    Text,

    /// <summary><c>integer</c>.</summary>
    Integer,

    /// <summary><c>boolean</c>.</summary>
    Boolean,
}

/// <summary>
/// One column of <c>applications.applications</c> a message may fill itself in
/// from.
/// </summary>
/// <remarks>
/// <see cref="Column"/> is the column name exactly as the table spells it. The
/// placeholder name an author types is derived from it in one place — see
/// <c>MergeFields.NameFor</c> — so <c>first_name</c> can only ever become
/// <c>{{firstName}}</c>.
/// <para>
/// <see cref="OnAddressLists"/> is what a typed list of addresses can supply,
/// which is an address and nothing else. The recipient there is frequently a
/// sponsor contact this system has never heard of, so a template that greets
/// people by name cannot be sent to one.
/// </para>
/// </remarks>
public sealed record ApplicantColumn(
    string Column,
    ColumnKind Kind,
    string Description,
    bool OnAddressLists = false);

/// <summary>
/// Every column of <c>applications.applications</c>, and whether a broadcast
/// may fill itself in from it.
/// </summary>
/// <remarks>
/// Here rather than in the API because a module owns its own tables, and this
/// is a statement about what is in one of them. <c>MergeFields</c> turns it
/// into placeholders; <see cref="PostgresSegmentResolver"/> selects exactly
/// <see cref="Mergeable"/> and nothing else, so a column nobody may merge is
/// never read out of the table in the first place.
/// <para>
/// Every column is listed, offered or withheld. That is the point rather than
/// an accident of being thorough: the divergence test compares this list
/// against the live schema and fails when they disagree, so adding a column to
/// the table forces somebody to say — once, here, in a sentence — whether it
/// may be mailed. A list that only named the offered ones would let a new
/// column be silently unavailable, and a list built by reading the schema at
/// request time would let a migration change the API without anybody
/// deciding to.
/// </para>
/// <para>
/// The line the withheld sentences are drawn on: a message may say back to
/// somebody what they told us about themselves. It may not say our bookkeeping
/// (ids, versions, timestamps), it may not carry a second copy of their
/// sensitive answers into <c>notify.messages</c>, and it may not quote a
/// storage key at them. Names and the address are the exception to the second
/// of those, because a message is addressed to and greets its recipient
/// whatever else it does.
/// </para>
/// </remarks>
public static class ApplicantColumns
{
    /// <summary>
    /// The address, named because two other things need to point at it.
    /// </summary>
    /// <remarks>
    /// <see cref="PostgresSegmentResolver"/> reads
    /// <see cref="SegmentMember.Email"/> out of this column, and a list of
    /// typed addresses fills this and only this.
    /// </remarks>
    public static readonly ApplicantColumn Address = new(
        "email",
        ColumnKind.Text,
        "The address this message is going to.",
        OnAddressLists: true);

    /// <summary>
    /// The columns a broadcast may fill itself in from, in the order an editor
    /// lists them.
    /// </summary>
    /// <remarks>
    /// Table order, which puts the three a template almost always wants first
    /// and the rest behind them. Alphabetical would open the list on
    /// <c>{{country}}</c>.
    /// </remarks>
    public static readonly IReadOnlyList<ApplicantColumn> Mergeable =
    [
        Address,

        new("first_name",
            ColumnKind.Text,
            "The recipient's first name, as their application has it."),

        new("last_name",
            ColumnKind.Text,
            "The recipient's last name, as their application has it."),

        new("school",
            ColumnKind.Text,
            "The school the recipient put on their application."),

        new("level_of_study",
            ColumnKind.Text,
            "The level of study the recipient put on their application."),

        new("graduation_year",
            ColumnKind.Integer,
            "The year the recipient expects to graduate, as their application "
            + "has it."),

        new("first_time_hacker",
            ColumnKind.Boolean,
            "Whether the recipient said this is their first hackathon. Fills "
            + "in as yes or no."),

        new("shirt_size",
            ColumnKind.Text,
            "The shirt size the recipient chose on their application."),

        new("country",
            ColumnKind.Text,
            "The country the recipient put on their application."),
    ];

    /// <summary>
    /// The columns a broadcast may not fill itself in from, and why not.
    /// </summary>
    /// <remarks>
    /// Sentences rather than categories, because the reason is the useful part
    /// when somebody adds a column and has to decide which side it lands on.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> Withheld =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ------------------------------------------------ bookkeeping ---
            ["id"] =
                "The row's own id. It names a record rather than a person, and "
                + "a message quoting it would be quoting our filing back at "
                + "somebody.",

            ["event_id"] =
                "Which cycle the application belongs to. The sender already "
                + "chose the event when they chose the segment, so this is the "
                + "same value in every message.",

            ["person_id"] =
                "The identity row behind the application. An internal id, and "
                + "null for everybody who applied without signing in.",

            ["form_version"] =
                "Which question set they answered. It exists to prove what "
                + "somebody agreed to and means nothing to the person reading "
                + "the mail.",

            ["decided_by"] =
                "The organizer who made the decision. Ours to record and not "
                + "theirs to be told.",

            ["checked_in_by"] =
                "The organizer who checked them in. Ours to record, like "
                + "decided_by.",

            ["responses"] =
                "The whole free-form answer set. There is no single value to "
                + "render, and Redaction.SensitiveKeys names it for the same "
                + "reason.",

            ["status"] =
                "Where the application is in its lifecycle. This picks who a "
                + "broadcast goes to rather than what it says — an "
                + "applicationStatus segment gives every recipient the same "
                + "value — and the stored spelling (under_review) is not what "
                + "a sentence wants.",

            ["age"] =
                "Their age. Like status, it decides who gets a message rather "
                + "than filling a blank in one; the under-18 rules are a "
                + "segment, not a sentence.",

            // -------------------------------------------------- their CV ---
            ["resume_key"] =
                "Where the bytes of somebody's CV are. A key in an email is a "
                + "way to read it for anybody the email is forwarded to, which "
                + "is why the column is a key and never a URL.",

            ["resume_filename"] =
                "What the file was called on the applicant's machine. "
                + "Attacker-controlled text kept only so a reviewer sees a name "
                + "they recognise.",

            ["resume_size"] =
                "The size of the file in bytes. Nothing a message says.",

            ["resume_uploaded_at"] =
                "When the file arrived. Bookkeeping, and a timestamp besides.",

            // ------------------------- theirs, and not the mail's to carry ---
            ["dietary_needs"] =
                "Theirs, and mailing it back to them would be defensible on its "
                + "own — but rendering happens at queue time and freezes the "
                + "result into notify.messages, which is a second copy in "
                + "another schema with different readers and a different "
                + "retention. Redaction.SensitiveKeys already says this value "
                + "does not leave applications.*. The address and the names are "
                + "the exception because a message is addressed to and greets "
                + "its recipient whatever else it does; this is not.",

            ["accessibility_needs"] =
                "Health-adjacent, and withheld for exactly the reason "
                + "dietary_needs is: merging it copies it into notify.messages "
                + "and leaves it there.",

            ["phone"] =
                "A contact detail, on Redaction.SensitiveKeys, and copied into "
                + "notify.messages by any message that merges it. A mail does "
                + "not need somebody's phone number to reach them.",

            // ------------------------------------------- what they agreed ---
            ["mlh_coc_agreed_at"] =
                "When they accepted MLH's code of conduct. A legal record, and "
                + "a timestamp with no timezone to render it in.",

            ["mlh_data_sharing_at"] =
                "When they agreed to data sharing. A legal record, like "
                + "mlh_coc_agreed_at.",

            ["mlh_marketing_opt_in"] =
                "Whether they opted in to marketing. Consent state is a record "
                + "we act on, not a line to print back at somebody — and "
                + "everybody who should be receiving a marketing broadcast has "
                + "the same value.",

            // -------------------------------------------- every timestamp ---
            // The near miss is rsvp_deadline, so it gets the long version and
            // the rest point at it.
            ["rsvp_deadline"] =
                "The one genuinely useful date here, and still withheld: it is "
                + "a timestamptz and nothing in this system knows the event's "
                + "timezone, so any rendering picks one on the reader's behalf. "
                + "A midnight deadline shown in UTC lands on the wrong calendar "
                + "day for exactly the people it matters to. A deadline belongs "
                + "in the copy an author writes, where they can name the zone "
                + "and mean it. When applications.events carries a timezone, "
                + "this is the first column to reconsider.",

            ["started_at"] =
                "When the form was first opened. Bookkeeping, and a timestamp "
                + "with no timezone to render it in — see rsvp_deadline.",

            ["submitted_at"] =
                "When they pressed submit. A timestamp with no timezone to "
                + "render it in — see rsvp_deadline.",

            ["decided_at"] =
                "When the decision was recorded. A timestamp, and internal "
                + "besides: the decision is the news, not the minute it was "
                + "typed.",

            ["confirmed_at"] =
                "When they confirmed their place. A timestamp — see "
                + "rsvp_deadline.",

            ["declined_at"] =
                "When they declined their place. A timestamp — see "
                + "rsvp_deadline.",

            ["checked_in_at"] =
                "When they arrived at the event. A timestamp — see "
                + "rsvp_deadline.",

            ["check_in_code"] =
                "The code they show at the door. Whoever holds it can have "
                + "its owner marked as arrived, and a broadcast is the one "
                + "place we could put several hundred of them into several "
                + "hundred inboxes at once. It lives on their portal screen, "
                + "behind a sign-in, which is where a link in a message "
                + "should send them.",

            ["check_in_code_issued_at"] =
                "When their check-in code was first shown to them. A "
                + "timestamp, and bookkeeping.",

            ["created_at"] =
                "When the row was written. Bookkeeping.",

            ["updated_at"] =
                "When the row last changed. Bookkeeping, and it moves whenever "
                + "anybody edits anything.",
        };

    /// <summary>
    /// Every column this file has an opinion about, offered or withheld.
    /// </summary>
    /// <remarks>
    /// What the divergence test compares against the live table. Any
    /// difference in either direction is a failure: a column in the table and
    /// not here is one nobody has decided about, and a column here and not in
    /// the table is a placeholder that would refuse at send.
    /// </remarks>
    public static readonly IReadOnlySet<string> Declared =
        new HashSet<string>(
            Mergeable.Select(column => column.Column).Concat(Withheld.Keys),
            StringComparer.Ordinal);
}
