namespace MorganHacks.Observability;

/// <summary>
/// Business signals, named once so an alert can be built on them.
/// </summary>
/// <remarks>
/// The failures worth alerting on here are absences, not spikes. If
/// <c>magic_link.requested</c> stays healthy while <c>magic_link.consumed</c>
/// collapses, mail is not arriving: every service is up, every dashboard is
/// green, and nobody can log in. No error rate catches that, because nothing
/// is erroring.
/// <para>
/// Emitted as a property on a log line rather than through a metrics library.
/// A counter needs somewhere to go, and one more thing to run is a worse trade
/// than a field an aggregator can already count.
/// </para>
/// </remarks>
public static class Events
{
    /// <summary>The property these appear under.</summary>
    public const string Property = "event";

    /// <summary>A sign-in link was genuinely queued for somebody.</summary>
    public const string MagicLinkRequested = "magic_link.requested";

    /// <summary>Somebody clicked one and got a session.</summary>
    public const string MagicLinkConsumed = "magic_link.consumed";

    /// <summary>A message was accepted by the provider.</summary>
    public const string MessageSent = "message.sent";

    /// <summary>Somebody finished an application.</summary>
    /// <remarks>
    /// The counter registration watches on the day. Paired with the form's own
    /// view count, a submission rate that falls away says the form is asking
    /// something people will not answer — which is a content problem no error
    /// rate reports.
    /// </remarks>
    public const string ApplicationSubmitted = "application.submitted";

    /// <summary>
    /// An organizer moved an application to a new status.
    /// </summary>
    /// <remarks>
    /// The record of the decision is <c>applications.status_history</c>, which
    /// a trigger writes inside the same transaction and which outlives any log
    /// retention window. This line is for the job a table cannot do: an alert
    /// fires on a line arriving, and four hundred of these in an evening is
    /// either the day decisions went out or somebody working through the queue
    /// with a script.
    /// <para>
    /// Carries both person ids and the two statuses. Never the reason, which is
    /// a sentence somebody wrote about an applicant — that lives on the history
    /// row, behind a permission, where a log line is not.
    /// </para>
    /// </remarks>
    public const string ApplicationStatusChanged = "application.status_changed";

    /// <summary>
    /// A resume was accepted and written to the object store.
    /// </summary>
    /// <remarks>
    /// Watched against <see cref="ApplicationSubmitted"/> rather than on its
    /// own. Uploads that keep arriving while submissions stop is somebody's
    /// resume failing to attach, which looks like nothing at all from the
    /// outside — every request succeeded and the applications simply have no
    /// resume on them.
    /// <para>
    /// Carries the form code, the upload id and the size. Never the filename:
    /// people name these after themselves.
    /// </para>
    /// </remarks>
    public const string ResumeStored = "resume.stored";

    /// <summary>A resume was handed to an organizer as a signed link.</summary>
    /// <remarks>
    /// The permission model treats a resume as more sensitive than the rest of
    /// an application, which is only true if reading one leaves a mark. This
    /// is that mark: who asked, for which application, and when.
    /// </remarks>
    public const string ResumeRead = "resume.read";

    /// <summary>
    /// Somebody took a copy of the answers out of the system.
    /// </summary>
    /// <remarks>
    /// <c>applications.export</c> is on the sensitive list because a
    /// spreadsheet on a laptop is PII we no longer control, and that is only
    /// meaningful if taking one leaves a record. This is that record: who,
    /// which form, and how many rows. Never what was in them.
    /// <para>
    /// Worth alerting on by volume rather than by absence, unlike most of this
    /// list. Exports are rare and deliberate, so several in an evening is
    /// either a launch week or something to ask about.
    /// </para>
    /// </remarks>
    public const string ResponsesExported = "responses.exported";

    /// <summary>An address was added to the suppression list.</summary>
    public const string AddressSuppressed = "address.suppressed";

    /// <summary>A broadcast was drafted. Nobody has been mailed.</summary>
    public const string CampaignCreated = "campaign.created";

    /// <summary>
    /// A broadcast was approved and its recipient list frozen.
    /// </summary>
    /// <remarks>
    /// The one line in this file worth waking somebody for. Everything else
    /// here is watched for absence; this is watched because it happened —
    /// several hundred emails have just entered the queue and cannot be
    /// recalled once lark starts draining them. It carries who drafted it, who
    /// approved it, and how many people it reached, which is the whole of what
    /// an after-the-fact question needs.
    /// </remarks>
    public const string CampaignQueued = "campaign.queued";

    /// <summary>A broadcast was stopped, and how much of it had already gone.</summary>
    public const string CampaignCancelled = "campaign.cancelled";

    /// <summary>
    /// A template was created or saved. Nobody has been mailed.
    /// </summary>
    /// <remarks>
    /// One event rather than a created and an edited, because the version
    /// number on the line already says which it was and a template's first
    /// version is not a different kind of fact from its fourth.
    /// <para>
    /// Watched for the same reason <c>campaign.queued</c> is, one step earlier:
    /// this is the line that explains why a campaign somebody drafted on Monday
    /// stopped being sendable on Tuesday. It carries the key, the version and
    /// who saved it, and never the subject or the body — those are wording, and
    /// wording belongs in the database rather than in a log somebody greps.
    /// </para>
    /// </remarks>
    public const string TemplateWritten = "template.written";

    /// <summary>
    /// Somebody changed somebody else's access.
    /// </summary>
    /// <remarks>
    /// The permission model requires that every grant change be attributable:
    /// "who gave this person export at 2am" must have an answer. That answer
    /// is now <c>audit.entries</c>, which the database writes inside the same
    /// transaction as the change and which outlives any log retention window.
    /// <para>
    /// These lines stay, for the job a table cannot do: an alert fires on a
    /// log line arriving, and a burst of <c>access.grant_changed</c> at 3am is
    /// something somebody should be woken by rather than something to notice
    /// during the next access review. They carry both person ids and never the
    /// address — <c>actor</c> did it, <c>subject</c> had it done to them.
    /// </para>
    /// </remarks>
    public const string OrganizerAdded = "access.organizer_added";

    /// <summary>A team membership was added, retimed, or removed.</summary>
    public const string TeamChanged = "access.team_changed";

    /// <summary>An individual grant was added, retimed, or removed.</summary>
    public const string GrantChanged = "access.grant_changed";

    /// <summary>Somebody was taken off the allowlist and their sessions cut.</summary>
    public const string PersonRevoked = "access.person_revoked";

    /// <summary>A form was made, and got the code that goes on a flyer.</summary>
    public const string FormCreated = "form.created";

    /// <summary>
    /// A draft's questions were written.
    /// </summary>
    /// <remarks>
    /// The builder autosaves, so this is chatty by design and is not worth
    /// alerting on by itself. It is worth having: when somebody asks why the
    /// form changed the evening before launch, this is the only record of who
    /// was typing and when.
    /// </remarks>
    public const string FormDraftSaved = "form.draft_saved";

    /// <summary>
    /// A form went live in front of applicants.
    /// </summary>
    /// <remarks>
    /// The one in this group worth watching. Every application records the
    /// version it answered, so a publish is the moment two applicants stop
    /// having been asked the same thing — and if one lands during registration
    /// somebody should know without being told.
    /// </remarks>
    public const string FormPublished = "form.published";

    /// <summary>
    /// A live form was taken down.
    /// </summary>
    /// <remarks>
    /// Worth its own event because the symptom is somebody reporting a link
    /// that used to work, and the cause is a deliberate act by an organizer
    /// rather than a fault. Without this the trail says only that the version
    /// was retired, which is also what publishing a new one does.
    /// </remarks>
    public const string FormUnpublished = "form.unpublished";
}
