import { apiFetch } from "@/lib/api";
import type {
  Placeholder,
  Rendered,
  Template,
  TemplateDraft,
  TemplateRow,
} from "@/components/templates/types";

/**
 * Talking to the templates API.
 *
 * Server-side only. Everything the browser needs goes through the actions
 * beside this file, so no component holds a URL to the API or decides what a
 * failure means.
 */

export type ListRead =
  | { ok: true; items: TemplateRow[]; mocked: boolean }
  | { ok: false; status: number; error: string };

export type OneRead =
  | { ok: true; template: Template; mocked: boolean }
  | { ok: false; status: number; error: string };

/**
 * What the API did with a write.
 *
 * `version` and `note` are the API's own answer, passed through rather than
 * summarised. An edit bumps the version, and what that means for a campaign
 * already pointed at this key is the API's business to decide and say — this
 * screen repeats it and does not paraphrase it.
 */
export type Saved =
  | { ok: true; key: string; version: number | null; note: string | null }
  | { ok: false; error: string };

export type PreviewRead =
  | { ok: true; rendered: Rendered }
  | { ok: false; error: string };

/**
 * The names a send can fill in.
 *
 * A failure here is not an error on the page. The editor simply stops offering
 * a menu and stops calling anything unknown, because the only thing worse than
 * not knowing which placeholders resolve is being told the wrong ones.
 */
export type PlaceholderRead =
  | { ok: true; items: Placeholder[]; mocked: boolean }
  | { ok: false; error: string };

/**
 * What to say about a request that did not work.
 *
 * `email.manage_templates` is named because it is the grant that is missing,
 * and it is not the one the compose screen names — somebody who can send a
 * broadcast cannot necessarily write one. Naming the wrong permission sends
 * somebody to an admin to ask for a grant they do not need.
 */
function why(status: number, fallback: string): string {
  if (status === 403) {
    return "You do not have email.manage_templates. Ask an admin.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return fallback;
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

/**
 * Whatever the API wants said about a write that worked.
 *
 * Read rather than assumed. The version an edit lands on is the API's to
 * decide, and if it has something to add about a template that has already
 * been used, that sentence is its own and is shown as it was written.
 */
function noted(body: Record<string, unknown>): string | null {
  for (const field of ["note", "warning", "message"]) {
    const value = body[field];
    if (typeof value === "string" && value !== "") {
      return value;
    }
  }

  return null;
}

/** Every template, as the API orders them. */
export async function readTemplates(): Promise<ListRead> {
  let response: Response;
  try {
    response = await apiFetch("/admin/templates");
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (response.status === 404 && EXAMPLES) {
    return { ok: true, items: exampleList(), mocked: true };
  }

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error: why(response.status, "Templates could not be loaded."),
    };
  }

  const { templates } = (await response.json()) as { templates: TemplateRow[] };
  return { ok: true, items: templates, mocked: false };
}

/** One template, with its body and everything rendered from it. */
export async function readTemplate(key: string): Promise<OneRead> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/templates/${encodeURIComponent(key)}`);
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (response.status === 404 && EXAMPLES) {
    const template = exampleOne(key);
    if (template) {
      return { ok: true, template, mocked: true };
    }
  }

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error: why(response.status, "That template could not be loaded."),
    };
  }

  const template = (await response.json()) as Template;
  return { ok: true, template, mocked: false };
}

/** Writes a template that did not exist. */
export async function createTemplate(draft: TemplateDraft): Promise<Saved> {
  return write("POST", "/admin/templates", draft);
}

/** Writes over a template that did. The API bumps the version. */
export async function updateTemplate(
  key: string,
  draft: TemplateDraft,
): Promise<Saved> {
  return write("PUT", `/admin/templates/${encodeURIComponent(key)}`, draft);
}

async function write(
  method: "POST" | "PUT",
  path: string,
  draft: TemplateDraft,
): Promise<Saved> {
  let response: Response;
  try {
    response = await apiFetch(path, {
      method,
      body: JSON.stringify(draft),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (response.status === 404 && EXAMPLES) {
    return exampleWrite(draft);
  }

  if (!response.ok) {
    return {
      ok: false,
      error: await said(response, why(response.status, "That did not work.")),
    };
  }

  let body: Record<string, unknown> = {};
  try {
    body = (await response.json()) as Record<string, unknown>;
  } catch {
    // A write that answered with no body still worked. The version is then
    // simply not known here, which is better than inventing one.
  }

  return {
    ok: true,
    key: typeof body.key === "string" ? body.key : draft.key,
    version: typeof body.version === "number" ? body.version : null,
    note: noted(body),
  };
}

/**
 * What the body would come out as.
 *
 * Rendered by the API on every keystroke's worth of pause rather than in the
 * browser. There is one markdown renderer in this system and it is the one the
 * sender uses; a second one here would agree with it right up until the day
 * somebody types the thing they disagree about, and the copy that goes to four
 * hundred people is the one this screen never showed.
 */
export async function renderPreview(input: {
  subject: string;
  markdown: string;
  values?: Record<string, string>;
}): Promise<PreviewRead> {
  let response: Response;
  try {
    response = await apiFetch("/admin/templates/preview", {
      method: "POST",
      body: JSON.stringify(input),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (response.status === 404 && EXAMPLES) {
    return { ok: true, rendered: examplePreview(input) };
  }

  if (!response.ok) {
    return {
      ok: false,
      error: await said(
        response,
        why(response.status, "The preview could not be rendered."),
      ),
    };
  }

  return { ok: true, rendered: (await response.json()) as Rendered };
}

/**
 * Which placeholders resolve, from the only thing that knows.
 *
 * Two endpoints behind one function. With no campaign this is the general
 * list, which is the editor's ordinary case: a template is written long before
 * anybody decides who it goes to. Given a campaign it is that campaign's,
 * narrowed to what its segment can actually fill.
 *
 * A campaign that cannot be read is never quietly widened to the general list.
 * The narrow list exists precisely because the general one contains names this
 * segment has no value for, so falling back would hand somebody a placeholder
 * that renders empty — or refuses — for the exact audience they were writing
 * to.
 */
export async function readPlaceholders(
  campaignId?: string | null,
): Promise<PlaceholderRead> {
  const path = campaignId
    ? `/admin/campaigns/${encodeURIComponent(campaignId)}/placeholders`
    : "/admin/templates/placeholders";

  let response: Response;
  try {
    response = await apiFetch(path);
  } catch {
    return { ok: false, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      error: why(response.status, "Placeholders could not be loaded."),
    };
  }

  let body: { placeholders?: unknown };
  try {
    body = (await response.json()) as { placeholders?: unknown };
  } catch {
    return { ok: false, error: "Placeholders could not be loaded." };
  }

  return { ok: true, items: named(body.placeholders), mocked: false };
}

/**
 * The list, taken apart rather than cast to.
 *
 * Every name in here ends up in a menu somebody inserts from, so a row without
 * a usable name is dropped instead of becoming `{{undefined}}` in an email.
 * A missing description is null and not an empty string, because the editor
 * lays the row out differently when there is nothing to say.
 */
function named(value: unknown): Placeholder[] {
  if (!Array.isArray(value)) {
    return [];
  }

  const items: Placeholder[] = [];

  for (const entry of value) {
    if (typeof entry !== "object" || entry === null) {
      continue;
    }

    const { name, description } = entry as {
      name?: unknown;
      description?: unknown;
    };

    if (typeof name !== "string" || name === "") {
      continue;
    }

    items.push({
      name,
      description:
        typeof description === "string" && description !== ""
          ? description
          : null,
    });
  }

  return items;
}

// ---------------------------------------------------------------------------
// Example data, until the API is there
// ---------------------------------------------------------------------------

/*
 * Everything below this line is scaffolding and is meant to be deleted.
 *
 * The templates endpoints are being built in parallel with these screens.
 * Rather than ship pages nobody can look at until they land, a 404 from them —
 * and only a 404 — is answered locally so the list, the editor, the preview,
 * the placeholder list and the empty state can all be reviewed.
 *
 * Two locks, both of which must be off for any of it to run: production is
 * excluded outright, and outside production it still takes TEMPLATE_EXAMPLES=1
 * in the environment. A missing endpoint in production is a fault and has to
 * read as one.
 *
 * Nothing here invents a template. The store starts empty, which is the state
 * the real one is in, and only holds what somebody types into the editor
 * during a session — no subject and no body is written anywhere in this file,
 * because the wording of the first email this system sends is not a
 * developer's to draft.
 *
 * The preview here is deliberately not markdown. It escapes the text and
 * breaks it into paragraphs, so an author can see their own words in the
 * message frame while the real renderer is being built. Formatting will not
 * work and is not meant to. When the endpoints land this block goes and
 * nothing above it changes.
 */

/** Never in production, and off by default everywhere else. */
const EXAMPLES =
  process.env.NODE_ENV !== "production" &&
  process.env.TEMPLATE_EXAMPLES === "1";

/** Empty until somebody writes one. Lost when the dev server restarts. */
const examples = new Map<string, Template>();

function exampleList(): TemplateRow[] {
  return [...examples.values()].map(({ key, kind, subject, version }) => ({
    key,
    kind,
    subject,
    version,
    updatedAt: null,
  }));
}

function exampleOne(key: string): Template | null {
  return examples.get(key) ?? null;
}

function exampleWrite(draft: TemplateDraft): Saved {
  const existing = examples.get(draft.key);
  const version = (existing?.version ?? 0) + 1;
  const { html, text } = examplePreview(draft);

  examples.set(draft.key, {
    ...draft,
    html,
    text,
    version,
    placeholders: placeholderNames(`${draft.subject}\n${draft.markdown}`),
  });

  return { ok: true, key: draft.key, version, note: null };
}

function examplePreview(input: {
  subject: string;
  markdown: string;
}): Rendered {
  const escaped = input.markdown
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");

  const html = escaped
    .split(/\n{2,}/)
    .filter((block) => block.trim() !== "")
    .map((block) => `<p>${block.replaceAll("\n", "<br>")}</p>`)
    .join("\n");

  return { subject: input.subject, html, text: input.markdown };
}

function placeholderNames(text: string): string[] {
  return [...new Set([...text.matchAll(/\{\{\s*([\w.]+)\s*\}\}/g)].map((m) => m[1]))];
}
