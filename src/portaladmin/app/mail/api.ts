import { apiFetch, currentPerson, type FormsView } from "@/lib/api";
import type {
  Campaign,
  CampaignRow,
  CampaignStatus,
  EventChoice,
  FormChoice,
  MessageProgress,
  PlaceholderCoverage,
  Preview,
  Render,
  Segment,
} from "@/components/mail/types";
import type { TemplateRow } from "@/components/templates/types";
import { readTemplates } from "@/app/templates/api";

/**
 * Talking to the campaigns API.
 *
 * Server-side only. Everything the browser needs goes through the actions
 * beside this file, so no component holds a URL to the API or decides what a
 * failure means.
 */

export type ListRead =
  | { ok: true; items: CampaignRow[]; mocked: boolean }
  | { ok: false; status: number; error: string };

export type OneRead =
  | {
      ok: true;
      campaign: Campaign;

      /** Null on a draft, which has written no messages. */
      messages: MessageProgress | null;

      /**
       * The frozen list, a corner of it. Null when this person cannot read
       * addresses — the API checks `email.manage_templates` for the sample
       * separately from `email.view_stats` for the numbers, and a screen that
       * showed an empty list instead of nothing would be reporting that the
       * campaign reached nobody.
       */
      sample: string[] | null;
      mocked: boolean;
    }
  | { ok: false; status: number; error: string };

export type Created = { ok: true; id: string } | { ok: false; error: string };

export type PreviewRead =
  | { ok: true; preview: Preview }
  | { ok: false; error: string };

export type Changed =
  | { ok: true; status: CampaignStatus; recipientCount: number }
  | { ok: false; error: string };

/**
 * What to say about a request that did not work.
 *
 * The two permissions are named separately because they are held by different
 * people: reading what has been sent is `email.view_stats`, which most of
 * comms has, and sending to several hundred people is `email.send_broadcast`,
 * which is on the sensitive list precisely because it is not. Naming the wrong
 * one sends somebody to an admin to ask for a grant they do not need.
 */
function whyRead(status: number): string {
  if (status === 403) {
    return "You do not have email.view_stats. Ask an admin.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return "Campaigns could not be loaded.";
}

function whyWrite(status: number): string {
  if (status === 403) {
    return "You do not have email.send_broadcast. Ask an admin.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return "That did not work.";
}

/**
 * A campaign exactly as the API describes one.
 *
 * Written out rather than assumed, because the two shapes had drifted: the API
 * has never sent `sentAt`, which is the field these screens read to fill the
 * "Sent" column, so every campaign that had gone out was rendering an em dash.
 * It sends `queuedAt` and `completedAt` instead, and which of those a reader
 * means by "sent" is a question this file answers once.
 */
type Described = {
  id: string;
  name: string;
  status: CampaignStatus;
  templateKey?: string | null;
  templateKind?: string | null;
  segment?: Segment | null;
  recipientCount: number;
  createdBy?: string | null;
  approvedBy?: string | null;
  queuedAt?: string | null;
  completedAt?: string | null;
  createdAt: string;
};

/**
 * When it went, from the two stamps the API keeps.
 *
 * `completedAt` where the sender has finished with it, `queuedAt` where it is
 * still working through the queue. Both are the moment a reader means by "this
 * left", and preferring the later of them means a campaign half-way through a
 * send reads as having started rather than as not having happened.
 */
function received(row: Described): Campaign {
  return {
    id: row.id,
    name: row.name,
    status: row.status,
    recipientCount: row.recipientCount,
    createdAt: row.createdAt,
    sentAt: row.completedAt ?? row.queuedAt ?? null,
    templateKey: row.templateKey ?? null,
    templateKind: row.templateKind ?? null,
    segment: row.segment ?? null,
    createdBy: row.createdBy ?? null,
    approvedBy: row.approvedBy ?? null,
  };
}

/** The API's own sentence about a refusal, where it gave one. */
async function said(response: Response, fallback: string): Promise<string> {
  try {
    const { error } = (await response.json()) as { error?: string };
    return error ?? fallback;
  } catch {
    return fallback;
  }
}

/** Every campaign, newest first as the API returns them. */
export async function readCampaigns(): Promise<ListRead> {
  let response: Response;
  try {
    response = await apiFetch("/admin/campaigns");
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (response.status === 404 && EXAMPLES) {
    return { ok: true, items: exampleList(), mocked: true };
  }

  if (!response.ok) {
    return { ok: false, status: response.status, error: whyRead(response.status) };
  }

  // The API calls it campaigns, not items. This screen called it items,
  // nothing typed the boundary between them, and the page threw on undefined
  // the first time it was opened against a real API.
  const { campaigns } = (await response.json()) as { campaigns: Described[] };
  return { ok: true, items: (campaigns ?? []).map(received), mocked: false };
}

/** One campaign, with its template and its segment. */
export async function readCampaign(id: string): Promise<OneRead> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/campaigns/${encodeURIComponent(id)}`);
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (response.status === 404 && EXAMPLES) {
    const stored = exampleOne(id);
    if (stored) {
      const campaign = asSeenBy(stored, (await currentPerson())?.personId ?? null);
      return {
        ok: true,
        campaign,
        messages: exampleProgress(campaign),
        sample: campaign.status === "draft" ? null : exampleAddresses(8),
        mocked: true,
      };
    }
  }

  if (!response.ok) {
    return { ok: false, status: response.status, error: whyRead(response.status) };
  }

  // Wrapped, alongside the message counts and the sample. Both were being
  // thrown away here: the counts are the only account of what actually
  // happened to a send, and the sample is the only answer left to "who did we
  // mail" once the segment has moved on.
  const body = (await response.json()) as {
    campaign: Described;
    messages?: MessageProgress | null;
    sample?: string[] | null;
  };

  return {
    ok: true,
    campaign: received(body.campaign),
    messages: body.messages ?? null,
    sample: body.sample ?? null,
    mocked: false,
  };
}

/** Starts a campaign as a draft. Nothing is sent by creating one. */
export async function createCampaign(body: {
  name: string;
  templateKey: string;
  segment: Segment;
}): Promise<Created> {
  let response: Response;
  try {
    response = await apiFetch("/admin/campaigns", {
      method: "POST",
      body: JSON.stringify(body),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (response.status === 404 && EXAMPLES) {
    return { ok: true, id: exampleCreate(body) };
  }

  if (!response.ok) {
    return {
      ok: false,
      error: await said(response, whyWrite(response.status)),
    };
  }

  const { id } = (await response.json()) as { id: string };
  return { ok: true, id };
}

/**
 * Who this campaign would go to if it were sent now.
 *
 * Resolved on every call rather than cached. A preview from ten minutes ago is
 * a different set of people from the one a send would reach, and the whole
 * value of this screen is that the number in front of somebody is the number
 * that will be mailed.
 */
export async function previewCampaign(id: string): Promise<PreviewRead> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/campaigns/${encodeURIComponent(id)}/preview`,
      { method: "POST" },
    );
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (response.status === 404 && EXAMPLES) {
    const preview = examplePreview(id);
    if (preview) {
      return { ok: true, preview };
    }
  }

  if (!response.ok) {
    return {
      ok: false,
      error: await said(
        response,
        response.status === 403
          ? whyRead(response.status)
          : "The recipients could not be resolved.",
      ),
    };
  }

  const preview = (await response.json()) as Preview;
  return { ok: true, preview: checked(preview) };
}

/**
 * A preview body, reduced to the parts this screen can actually render.
 *
 * The cast above is a promise, not a check, and two of the fields it promises
 * are new: an API that has not shipped them yet sends nothing, and one that
 * has sends them in a shape this screen has never seen. Everywhere else that
 * costs a wrong number on a panel; here it costs a crash, because the coverage
 * list decides whether a warning appears at all and the renders are put
 * through an iframe.
 *
 * So the rule is the same for both: a field that is not the shape it was
 * promised as leaves this function absent rather than half-trusted. Absent is
 * a state the screen already knows how to be in — it was the only state before
 * these fields existed.
 */
function checked(preview: Preview): Preview {
  return {
    ...preview,
    sample: strings(preview.sample),
    problems: Array.isArray(preview.problems)
      ? strings(preview.problems)
      : undefined,
    placeholderCoverage: coverage(preview.placeholderCoverage),
    renders: renders(preview.renders),
  };
}

function strings(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === "string")
    : [];
}

function counted(value: unknown): number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0
    ? Math.trunc(value)
    : 0;
}

function coverage(value: unknown): PlaceholderCoverage[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }

  return value
    .filter(
      (entry): entry is PlaceholderCoverage =>
        typeof entry?.placeholder === "string" && entry.placeholder !== "",
    )
    .map((entry) => ({
      placeholder: entry.placeholder,
      missing: counted(entry.missing),
      total: counted(entry.total),
      examples: strings(entry.examples),
    }));
}

function renders(value: unknown): Render[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }

  return value
    .filter(
      (entry): entry is Render =>
        typeof entry?.email === "string" &&
        typeof entry.html === "string" &&
        typeof entry.text === "string",
    )
    .map((entry) => ({
      email: entry.email,
      subject: typeof entry.subject === "string" ? entry.subject : "",
      html: entry.html,
      text: entry.text,
      unfilled: strings(entry.unfilled),
    }));
}

/** Hands the campaign to the sender. There is no undo past this. */
export async function sendCampaign(id: string): Promise<Changed> {
  return change(id, "send");
}

/** Stops a campaign that is queued. */
export async function cancelCampaign(id: string): Promise<Changed> {
  return change(id, "cancel");
}

async function change(id: string, verb: "send" | "cancel"): Promise<Changed> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/campaigns/${encodeURIComponent(id)}/${verb}`,
      { method: "POST" },
    );
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (response.status === 404 && EXAMPLES) {
    const changed = exampleChange(id, verb, (await currentPerson())?.personId ?? null);
    if (changed) {
      return changed;
    }
  }

  if (!response.ok) {
    return {
      ok: false,
      error: await said(response, whyWrite(response.status)),
    };
  }

  // The API answers with queued -- how many messages were written -- rather
  // than recipientCount. They are the same number and it used the clearer
  // name; reading the wrong one showed "sent to 0 recipients" after a send
  // that had just written several hundred rows.
  const body = (await response.json()) as {
    status: CampaignStatus;
    queued?: number;
    suppressed?: number;
    recipientCount?: number;
  };

  return {
    ok: true,
    status: body.status,
    recipientCount: body.queued ?? body.recipientCount ?? 0,
  };
}

/**
 * The forms, for the segment that picks one.
 *
 * Failure is not fatal. Without `applications.view` this comes back empty, the
 * form segment offers nothing, and the other two still work — which is less
 * useful than the whole screen and better than none of it.
 */
export async function readForms(): Promise<{
  forms: FormChoice[];
  events: EventChoice[];
}> {
  try {
    const response = await apiFetch("/admin/forms");
    if (!response.ok) {
      return { forms: [], events: [] };
    }

    // The events ride along with the forms rather than being fetched
    // separately, because that endpoint already returns them and an
    // applicantStatus segment cannot be built without one.
    const { forms, events } = (await response.json()) as FormsView & {
      events?: EventChoice[];
    };

    return {
      forms: forms.map((form) => ({ id: form.id, name: form.name })),
      events: (events ?? []).map((event) => ({ id: event.id, name: event.name })),
    };
  } catch {
    return { forms: [], events: [] };
  }
}

/**
 * The templates a campaign is allowed to send.
 *
 * Broadcast only, filtered here rather than on the screen. A campaign given a
 * transactional template is refused when somebody tries to send it, with a
 * message about a lane and a subdomain that reads like a bug — and by then the
 * campaign has been named, segmented and previewed. Offering only what can be
 * chosen means the refusal cannot happen.
 *
 * The list is the templates screen's fetch, not a second one. One reader of
 * the endpoint means one answer to what a template is, and one place the
 * scaffolding has to be deleted from.
 *
 * The error comes back rather than an empty list, because "there are no
 * templates yet" and "you are not allowed to see them" put somebody on
 * completely different errands.
 */
export async function readBroadcastTemplates(): Promise<{
  templates: TemplateRow[];
  error: string | null;
}> {
  const read = await readTemplates();

  if (!read.ok) {
    return { templates: [], error: read.error };
  }

  return {
    templates: read.items.filter((template) => template.kind === "broadcast"),
    error: null,
  };
}

// ---------------------------------------------------------------------------
// Example data, until the API is there
// ---------------------------------------------------------------------------

/*
 * Everything below this line is scaffolding and is meant to be deleted.
 *
 * The campaigns endpoints are being built in parallel with these screens.
 * Rather than ship pages nobody can look at until they land, a 404 from them —
 * and only a 404 — is answered with fabricated campaigns so the list, the
 * compose panel, the preview gate, the send confirmation, the cancel and the
 * empty state can all be reviewed.
 *
 * Two locks, both of which must be off for a single example to appear:
 * production is excluded outright, and outside production it still takes
 * MAIL_EXAMPLES=1 in the environment. A missing endpoint in production is a
 * fault and has to read as one — a screen that quietly invents four hundred
 * recipients there is worse than a screen that says it could not load.
 *
 * The addresses are example.edu and the campaign names say what they are, so
 * nothing here can be mistaken for a real send. When the endpoints land this
 * block goes and nothing above it changes.
 */

/** Never in production, and off by default everywhere else. */
const EXAMPLES =
  process.env.NODE_ENV !== "production" && process.env.MAIL_EXAMPLES === "1";

/** Fixed, so the same page renders the same way on the server and the client. */
const EXAMPLE_NOW = Date.UTC(2026, 8, 1, 15, 20);

/** Somebody else. Every example but one was drafted by this person. */
const EXAMPLE_AUTHOR = "a41e94ab-0000-4000-8000-000000000001";

/** The second name on an example that has gone out. */
const EXAMPLE_APPROVER = "b38f29cd-0000-4000-8000-000000000002";

/**
 * Stands in for whoever is reading, and is swapped for their real id on the
 * way out. A fixture cannot know the signed-in person at the time it is
 * written, and the two-person refusal is only visible when it does.
 */
const EXAMPLE_SELF = "example-self";

/**
 * The API's sentence when the author tries to send their own campaign.
 *
 * Copied from CampaignEndpoints.Send rather than written here. The screen
 * shows the API's wording in every other refusal, and an example that read
 * differently from the real thing would be teaching the wrong sentence to the
 * people who see it first.
 */
const EXAMPLE_SELF_SEND_REFUSAL =
  "A broadcast has to be sent by somebody other than the person who wrote it. " +
  "Ask another organizer with broadcast permission to send this one.";

/** Fills the reader's own id in where a fixture could only leave a placeholder. */
function asSeenBy(campaign: Campaign, me: string | null): Campaign {
  return campaign.createdBy === EXAMPLE_SELF
    ? { ...campaign, createdBy: me ?? EXAMPLE_SELF }
    : campaign;
}

function exampleStamp(minutesAgo: number): string {
  return new Date(EXAMPLE_NOW - minutesAgo * 60_000).toISOString();
}

/**
 * The example campaigns, kept in the module so a send made against them is
 * still sent when the page re-renders. Lost whenever the dev server restarts,
 * which is the right amount of durability for a fixture.
 */
const examples = new Map<string, Campaign>();

function seed(): void {
  if (examples.size > 0) {
    return;
  }

  for (const campaign of [
    {
      id: "example-draft",
      name: "Example campaign (draft)",
      status: "draft" as const,
      recipientCount: 0,
      createdAt: exampleStamp(90),
      sentAt: null,
      templateKey: "example_template",
      templateKind: "broadcast",
      createdBy: EXAMPLE_AUTHOR,
      approvedBy: null,
      segment: {
        type: "applicationStatus",
        eventId: "00000000-0000-0000-0000-000000000000",
        statuses: ["accepted"],
      } as Segment,
    },
    {
      // The state the brief calls the one that will confuse people first: a
      // draft whose author is whoever is reading it. Every other example can
      // be sent; this one is refused, in the API's own words, and it exists so
      // that refusal can be looked at before somebody meets it for real.
      id: "example-yours",
      name: "Example campaign (drafted by you)",
      status: "draft" as const,
      recipientCount: 0,
      createdAt: exampleStamp(20),
      sentAt: null,
      templateKey: "example_template",
      templateKind: "broadcast",
      createdBy: EXAMPLE_SELF,
      approvedBy: null,
      segment: {
        type: "applicationStatus",
        eventId: "00000000-0000-0000-0000-000000000000",
        statuses: ["waitlisted"],
      } as Segment,
    },
    {
      id: "example-queued",
      name: "Example campaign (queued)",
      status: "queued" as const,
      recipientCount: 342,
      createdAt: exampleStamp(240),
      sentAt: exampleStamp(200),
      templateKey: "example_template",
      templateKind: "broadcast",
      createdBy: EXAMPLE_AUTHOR,
      approvedBy: EXAMPLE_APPROVER,
      segment: {
        type: "applicationStatus",
        eventId: "00000000-0000-0000-0000-000000000000",
        statuses: ["submitted"],
      } as Segment,
    },
    {
      id: "example-sent",
      name: "Example campaign (sent)",
      status: "sent" as const,
      recipientCount: 118,
      createdAt: exampleStamp(4_320),
      sentAt: exampleStamp(4_280),
      templateKey: "example_template",
      templateKind: "broadcast",
      createdBy: EXAMPLE_AUTHOR,
      approvedBy: EXAMPLE_APPROVER,
      segment: {
        type: "explicitList",
        emails: exampleAddresses(118),
      } as Segment,
    },
  ]) {
    examples.set(campaign.id, campaign);
  }
}

/**
 * How far through the queue an example campaign is.
 *
 * Derived from its status rather than stored, so the numbers agree with the
 * pill beside them. A draft has written no messages at all, which is why it
 * gets null instead of a row of noughts.
 */
function exampleProgress(campaign: Campaign): MessageProgress | null {
  if (campaign.status === "draft") {
    return null;
  }

  const total = campaign.recipientCount;

  if (campaign.status === "queued" || campaign.status === "sending") {
    const gone = Math.floor(total * 0.4);
    return {
      total,
      pending: total - gone,
      gone,
      byStatus: { pending: total - gone, sent: gone },
    };
  }

  if (campaign.status === "sent") {
    return {
      total,
      pending: 0,
      gone: total,
      byStatus: { delivered: total - 2, bounced: 2 },
    };
  }

  return { total, pending: 0, gone: 0, byStatus: { suppressed: total } };
}

function exampleAddresses(count: number): string[] {
  return Array.from({ length: count }, (_, index) => `person${index + 1}@example.edu`);
}

function exampleList(): CampaignRow[] {
  seed();

  return [...examples.values()]
    .map(({ id, name, status, recipientCount, createdAt, sentAt }) => ({
      id,
      name,
      status,
      recipientCount,
      createdAt,
      sentAt,
    }))
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
}

function exampleOne(id: string): Campaign | null {
  seed();
  return examples.get(id) ?? null;
}

function exampleCreate(body: {
  name: string;
  templateKey: string;
  segment: Segment;
}): string {
  seed();

  const id = `example-${examples.size + 1}`;
  examples.set(id, {
    id,
    name: body.name,
    status: "draft",
    recipientCount: 0,
    createdAt: exampleStamp(0),
    sentAt: null,
    templateKey: body.templateKey,
    segment: body.segment,
  });

  return id;
}

/**
 * A count that follows from the segment rather than a constant, so the number
 * on the confirmation is one that changes when the segment does.
 */
function examplePreview(id: string): Preview | null {
  const campaign = exampleOne(id);
  if (!campaign?.segment) {
    return null;
  }

  const segment = campaign.segment;
  const segmentSize =
    segment.type === "explicitList"
      ? segment.emails.length
      : segment.type === "formRespondents"
        ? 47
        : 20 + segment.statuses.join().length * 17;

  /*
   * A few of them held back, because a preview where matched and sendable are
   * always the same number never shows the sentence the whole panel is for —
   * "412 matched, 400 will be sent, 12 suppressed" — and that is the one a
   * reviewer needs to look at.
   *
   * The reasons are the four the suppressions table's check constraint allows,
   * written as it stores them.
   */
  const byReason: Record<string, number> = {};
  const suppressedCount = Math.min(segmentSize, 12);

  if (suppressedCount > 0) {
    byReason.unsubscribed = Math.ceil(suppressedCount / 2);
    byReason.hard_bounce = Math.floor(suppressedCount / 3);
    byReason.complaint =
      suppressedCount - byReason.unsubscribed - byReason.hard_bounce;
  }

  const recipientCount = segmentSize - suppressedCount;

  return {
    recipientCount,
    segmentSize,
    suppressedCount,
    suppressedByReason: byReason,
    sample: exampleAddresses(Math.min(recipientCount, 8)),
    problems: [],
    placeholderCoverage: exampleCoverage(recipientCount),
    renders: exampleRenders(recipientCount),
  };
}

/**
 * Two placeholders, one of them with a gap.
 *
 * A fixture where everything is filled never shows the panel doing its job,
 * and the job is the twelve people who would open an email addressed to
 * `{{firstName}}`. So one placeholder the segment covers and one it does not,
 * which is also the shape of the real problem: the field exists on some
 * records and not others.
 */
function exampleCoverage(recipientCount: number): PlaceholderCoverage[] {
  const missing = Math.min(recipientCount, 12);

  return [
    {
      placeholder: "firstName",
      missing,
      total: recipientCount,
      examples: exampleAddresses(Math.min(missing, 3)),
    },
    {
      placeholder: "eventName",
      missing: 0,
      total: recipientCount,
      examples: [],
    },
  ];
}

/**
 * Four messages, the last of which is one of the twelve.
 *
 * No wording. The bodies are the substituted values and nothing else — the
 * real ones come from the template, which belongs to whoever writes the
 * emails, and a fixture that invented copy would be putting words nobody
 * approved on a screen whose whole purpose is checking what goes out.
 */
function exampleRenders(recipientCount: number): Render[] {
  return exampleAddresses(Math.min(recipientCount, 4)).map(
    (email, index, all) => {
      const unfilled = index === all.length - 1 ? ["firstName"] : [];
      const name =
        unfilled.length > 0 ? "{{firstName}}" : `Example Person ${index + 1}`;

      return {
        email,
        subject: `Example Event · ${name}`,
        html: `<h1>Example Event</h1>\n<p>${name}</p>`,
        text: `Example Event\n\n${name}`,
        unfilled,
      };
    },
  );
}

function exampleChange(
  id: string,
  verb: "send" | "cancel",
  me: string | null,
): Changed | null {
  const campaign = exampleOne(id);
  if (!campaign) {
    return null;
  }

  if (verb === "send") {
    // The refusal the real API gives, given here for the same reason: the
    // person pressing this wrote the campaign, and the fixture would be
    // useless if it were the one example where that was allowed.
    if (campaign.createdBy === EXAMPLE_SELF && me !== null) {
      return { ok: false, error: EXAMPLE_SELF_SEND_REFUSAL };
    }

    const preview = examplePreview(id);
    const recipientCount = preview?.recipientCount ?? 0;

    examples.set(id, {
      ...campaign,
      status: "queued",
      recipientCount,
      sentAt: exampleStamp(0),
    });

    return { ok: true, status: "queued", recipientCount };
  }

  examples.set(id, { ...campaign, status: "cancelled", sentAt: null });
  return { ok: true, status: "cancelled", recipientCount: campaign.recipientCount };
}
