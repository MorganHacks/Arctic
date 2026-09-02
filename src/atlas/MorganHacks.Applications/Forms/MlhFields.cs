namespace MorganHacks.Applications.Forms;

/// <summary>
/// The questions MLH affiliation requires, and their exact wording.
/// </summary>
/// <remarks>
/// Every new form starts with these and cannot be published without them. The
/// failure this prevents is somebody tidying the form the week before launch
/// and quietly removing an obligation — discovered at the export, when there is
/// no way to ask several hundred people again.
/// <para>
/// The two required agreements store timestamps rather than booleans. "They
/// agreed" is weaker evidence than "they agreed at 14:03 on the 12th, against
/// form version 3", and this is a legal agreement we may have to show.
/// </para>
/// </remarks>
public static class MlhFields
{
    /// <summary>MLH's required wording. Not ours to reword.</summary>
    public const string CodeOfConductLabel =
        "I have read and agree to the MLH Code of Conduct.";

    public const string DataSharingLabel =
        "I authorize you to share my application/registration information with "
        + "Major League Hacking for event administration, ranking, and MLH/DEV "
        + "administration (including the creation of linked accounts on MLH and "
        + "DEV) in line with the MLH Privacy Policy. I further agree to the terms "
        + "of both the MLH Contest Terms and Conditions and the MLH Privacy Policy.";

    public const string MarketingLabel =
        "I authorize MLH to send me occasional emails about relevant events, "
        + "career opportunities, and community announcements.";

    public static IReadOnlyList<FormField> All { get; } =
    [
        // First because everything else hangs off it. `applications.email` is
        // NOT NULL and the dedupe index is built on it, so a form that does
        // not ask for an address cannot create an applicant at all — and it is
        // also the only way to tell somebody they got in.
        Column("email", FieldType.Email, "Email", required: true),

        Column("first_name", FieldType.ShortText, "First name", required: true),
        Column("last_name", FieldType.ShortText, "Last name", required: true),
        Column("age", FieldType.Number, "Age", required: true),
        Column("phone", FieldType.Phone, "Phone number", required: true),
        Column("school", FieldType.ShortText, "School", required: true),
        Column("country", FieldType.ShortText, "Country of residence", required: true),

        new FormField
        {
            Key = "level_of_study",
            Type = FieldType.Select,
            Label = "Current level of study",
            Required = true,
            Locked = true,
            Storage = AnswerStorage.Column,
            Column = "level_of_study",
            Options =
            [
                new("less-than-secondary", "Less than secondary / high school"),
                new("secondary", "Secondary / high school"),
                new("undergraduate-2y", "Undergraduate university (2 year)"),
                new("undergraduate-3y", "Undergraduate university (3+ year)"),
                new("graduate", "Graduate university (Masters, Doctoral, etc)"),
                new("bootcamp", "Code school / bootcamp"),
                new("vocational", "Other vocational / trade program"),
                new("post-doctorate", "Post doctorate"),
                new("other", "Other"),
                new("not-a-student", "I'm not currently a student"),
                new("prefer-not-to-say", "Prefer not to answer"),
            ],
        },

        // Required. An application cannot be submitted without them, which the
        // completeness constraint enforces independently of anything the form does.
        Agreement("mlh_coc_agreed_at", CodeOfConductLabel, required: true),
        Agreement("mlh_data_sharing_at", DataSharingLabel, required: true),

        // Optional, and a boolean rather than a timestamp: nobody needs to
        // evidence when somebody opted into marketing.
        new FormField
        {
            Key = "mlh_marketing_opt_in",
            Type = FieldType.Consent,
            Label = MarketingLabel,
            Required = false,
            Locked = true,
            Storage = AnswerStorage.Column,
            Column = "mlh_marketing_opt_in",
        },
    ];

    /// <summary>The keys a form must contain before it can be published.</summary>
    public static IReadOnlySet<string> RequiredKeys { get; } =
        All.Where(f => f.Required).Select(f => f.Key).ToHashSet();

    private static FormField Column(
        string key, FieldType type, string label, bool required) => new()
        {
            Key = key,
            Type = type,
            Label = label,
            Required = required,
            Locked = true,
            Storage = AnswerStorage.Column,
            Column = key,
        };

    private static FormField Agreement(string key, string label, bool required) => new()
    {
        Key = key,
        Type = FieldType.Consent,
        Label = label,
        Required = required,
        Locked = true,
        Storage = AnswerStorage.Column,
        Column = key,
    };
}
