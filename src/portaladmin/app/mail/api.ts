import { apiFetch, type FormsView } from "@/lib/api";
import type { Campaign, CampaignRow, CampaignStatus, EventChoice, FormChoice, Preview, Segment } from "@/components/mail/types";
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
  | { ok: true; campaign: Campaign; mocked: boolean }
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

  const { items } = (await response.json()) as { items: CampaignRow[] };
  return { ok: true, items, mocked: false };
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
    const campaign = exampleOne(id);
    if (campaign) {
      return { ok: true, campaign, mocked: true };
    }
  }

  if (!response.ok) {
    return { ok: false, status: response.status, error: whyRead(response.status) };
  }

  const campaign = (await response.json()) as Campaign;
  return { ok: true, campaign, mocked: false };
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
  return { ok: true, preview };
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
    const changed = exampleChange(id, verb);
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

  const body = (await response.json()) as {
    status: CampaignStatus;
    recipientCount?: number;
  };

  return { ok: true, status: body.status, recipientCount: body.recipientCount ?? 0 };
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
      segment: {
        type: "applicationStatus",
        eventId: "00000000-0000-0000-0000-000000000000",
        statuses: ["accepted"],
      } as Segment,
    },
    {
      id: "example-queued",
      name: "Example campaign (queued)",
      status: "queued" as const,
      recipientCount: 342,
      createdAt: exampleStamp(240),
      sentAt: null,
      templateKey: "example_template",
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
      segment: {
        type: "explicitList",
        emails: exampleAddresses(118),
      } as Segment,
    },
  ]) {
    examples.set(campaign.id, campaign);
  }
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
  const recipientCount =
    segment.type === "explicitList"
      ? segment.emails.length
      : segment.type === "formRespondents"
        ? 47
        : 20 + segment.statuses.join().length * 17;

  return {
    recipientCount,
    sample: exampleAddresses(Math.min(recipientCount, 8)),
  };
}

function exampleChange(id: string, verb: "send" | "cancel"): Changed | null {
  const campaign = exampleOne(id);
  if (!campaign) {
    return null;
  }

  if (verb === "send") {
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
