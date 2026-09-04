namespace MorganHacks.Identity.Domain;

/// <summary>
/// A single thing someone is allowed to do.
/// </summary>
/// <remarks>
/// This type is the source of truth for what permissions exist. The database
/// stores <see cref="Value"/> as text and does not constrain it, because a
/// check constraint would mean a migration every time a permission is added.
/// Validation happens here instead, via <see cref="TryParse"/>.
/// <para>
/// Grouped by the data they touch rather than by team, so that reorganising
/// teams is a data change rather than a code change.
/// </para>
/// </remarks>
public readonly record struct Permission(string Value)
{
    public override string ToString() => Value;

    // Applications
    public static readonly Permission ApplicationsView = new("applications.view");
    public static readonly Permission ApplicationsDecide = new("applications.decide");
    public static readonly Permission ApplicationsBulkDecide = new("applications.bulk_decide");

    /// <summary>PII leaves the system. Treat as sensitive.</summary>
    public static readonly Permission ApplicationsExport = new("applications.export");

    /// <summary>Separate from view: resumes are more sensitive than the rest of a record.</summary>
    public static readonly Permission ApplicationsViewResume = new("applications.view_resume");

    /// <summary>
    /// Reading the answers people gave.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ApplicationsView"/> for the same reason
    /// <see cref="ApplicationsViewResume"/> is. <c>applications.view</c> is a
    /// large group — comms holds it to build segments, logistics holds it for
    /// headcount and dietary needs, and it also gates the form builder, where
    /// seeing the questions is not seeing anybody's answers to them. Reading
    /// what several hundred people wrote about themselves is a narrower thing
    /// than any of that.
    /// <para>
    /// Not on the sensitive list. Reading answers on a screen leaves them in
    /// the system; <see cref="ApplicationsExport"/> is the one that takes a
    /// copy out, and that is the permission the CSV is behind.
    /// </para>
    /// </remarks>
    public static readonly Permission ApplicationsViewResponses =
        new("applications.view_responses");

    public static readonly Permission ApplicationsNote = new("applications.note");

    // Forms

    /// <summary>
    /// Building the form applicants answer.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ApplicationsView"/>, which every team that
    /// reads the queue holds. Reading applications is a large group; deciding
    /// what several hundred people will be asked, once, with no way to correct
    /// it for the ones who already answered, is a small one.
    /// <para>
    /// Not on the sensitive list. It changes what is collected rather than
    /// moving anything already collected out of the system, and a confirmation
    /// step on every keystroke of an autosaving editor would be noise.
    /// </para>
    /// </remarks>
    public static readonly Permission FormsManage = new("forms.manage");

    // Events

    /// <summary>
    /// Creating the year's event, and setting its dates and capacity.
    /// </summary>
    /// <remarks>
    /// Its own permission rather than a reuse of
    /// <see cref="PeopleGrantPermissions"/> or <see cref="AuditView"/>, which
    /// are the two nobody but super admin holds and so were the obvious
    /// candidates. The audience is the same today; the sentence is not.
    /// A permission is the string an admin reads on a grant screen, and
    /// "give them people.grant_permissions so they can set the registration
    /// dates" hands somebody the ability to change everybody's access in order
    /// to get one date field.
    /// <para>
    /// Held by super admin alone. An event is the root forms, applications and
    /// campaign segments all hang off, there is one a year, and its
    /// registration dates decide who can apply at all.
    /// </para>
    /// <para>
    /// Not on the sensitive list. It moves no PII out of the system and
    /// changes nobody's access, which is what that list is for.
    /// </para>
    /// </remarks>
    public static readonly Permission EventsManage = new("events.manage");

    // Email
    public static readonly Permission EmailSendTemplated = new("email.send_templated");
    public static readonly Permission EmailSendBroadcast = new("email.send_broadcast");
    public static readonly Permission EmailManageTemplates = new("email.manage_templates");
    public static readonly Permission EmailViewStats = new("email.view_stats");

    // Sponsors
    public static readonly Permission SponsorsView = new("sponsors.view");
    public static readonly Permission SponsorsEdit = new("sponsors.edit");
    public static readonly Permission SponsorsViewFinancials = new("sponsors.view_financials");

    // Event ops — phase two
    public static readonly Permission CheckinScan = new("checkin.scan");
    public static readonly Permission SwagScan = new("swag.scan");
    public static readonly Permission CheckinViewStats = new("checkin.view_stats");

    // Judging — phase two
    public static readonly Permission JudgingScoreAssigned = new("judging.score_assigned");
    public static readonly Permission JudgingViewAll = new("judging.view_all");
    public static readonly Permission JudgingAssign = new("judging.assign");

    // Admin
    public static readonly Permission PeopleView = new("people.view");
    public static readonly Permission PeopleManageTeams = new("people.manage_teams");
    public static readonly Permission PeopleGrantPermissions = new("people.grant_permissions");
    public static readonly Permission AuditView = new("audit.view");

    /// <summary>
    /// The four that deserve a confirmation step and a notification, because
    /// they either move PII out of the system or change who can do so.
    /// </summary>
    public static readonly IReadOnlySet<Permission> Sensitive = new HashSet<Permission>
    {
        ApplicationsExport,
        EmailSendBroadcast,
        PeopleGrantPermissions,
        SponsorsViewFinancials,
    };

    public static readonly IReadOnlySet<Permission> All = new HashSet<Permission>
    {
        ApplicationsView, ApplicationsDecide, ApplicationsBulkDecide,
        ApplicationsExport, ApplicationsViewResume, ApplicationsViewResponses,
        ApplicationsNote,
        FormsManage,
        EventsManage,
        EmailSendTemplated, EmailSendBroadcast, EmailManageTemplates, EmailViewStats,
        SponsorsView, SponsorsEdit, SponsorsViewFinancials,
        CheckinScan, SwagScan, CheckinViewStats,
        JudgingScoreAssigned, JudgingViewAll, JudgingAssign,
        PeopleView, PeopleManageTeams, PeopleGrantPermissions, AuditView,
    };

    /// <summary>
    /// Reads a permission string from the database. Returns false for anything
    /// not in <see cref="All"/>, so a row left behind by a removed permission
    /// is ignored rather than silently granting something unknown.
    /// </summary>
    public static bool TryParse(string? value, out Permission permission)
    {
        permission = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = new Permission(value.Trim());
        if (!All.Contains(candidate))
        {
            return false;
        }

        permission = candidate;
        return true;
    }
}
