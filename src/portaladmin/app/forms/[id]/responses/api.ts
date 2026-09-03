import { apiFetch, type DraftView, type FormField } from "@/lib/api";
import type { ResponseItem, ResponsePage } from "@/components/responses/types";

/**
 * Reading submissions.
 *
 * Server-side only. Everything the browser needs goes through the actions in
 * this folder, so no component ever holds a URL to the API or decides what a
 * failure means.
 */

/**
 * How many responses come back at a time.
 *
 * Fifty is about two screens of rows. Small enough that the first page is on
 * screen quickly on the morning registration closes, large enough that reading
 * a few hundred is a handful of clicks rather than a chore.
 */
const PAGE = 50;

export type PageRead =
  | { ok: true; page: ResponsePage; mocked: boolean }
  | { ok: false; status: number; error: string };

export type ItemRead =
  | { ok: true; item: ResponseItem; mocked: boolean }
  | { ok: false; status: number; error: string };

/**
 * What to say about a request that did not work.
 *
 * 403 names the permission, for the same reason the rest of this console does:
 * it turns "it doesn't work" into a request an admin can act on.
 */
function why(status: number): string {
  if (status === 403) {
    return "You do not have applications.view. Ask an admin.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return "Responses could not be loaded.";
}

/** One page of submissions, newest first. */
export async function readPage(
  formId: string,
  cursor: string | null,
): Promise<PageRead> {
  const query = new URLSearchParams({ limit: String(PAGE) });
  if (cursor !== null && cursor !== "") {
    query.set("cursor", cursor);
  }

  let response: Response;
  try {
    response = await apiFetch(`/admin/forms/${formId}/responses?${query}`);
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (response.status === 404 && EXAMPLES) {
    return { ok: true, page: await examplePage(formId, cursor), mocked: true };
  }

  if (!response.ok) {
    return { ok: false, status: response.status, error: why(response.status) };
  }

  const page = (await response.json()) as ResponsePage;
  return { ok: true, page, mocked: false };
}

/**
 * One submission, with its resume link.
 *
 * Asked for when somebody opens a response rather than with the list. The link
 * is signed and valid about five minutes; minting fifty of them to draw a
 * table would leave fifty live links behind to read none of the files.
 */
export async function readOne(
  formId: string,
  responseId: string,
): Promise<ItemRead> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/forms/${formId}/responses/${encodeURIComponent(responseId)}`,
    );
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (response.status === 404 && EXAMPLES) {
    const item = await exampleItem(formId, responseId);
    if (item) {
      return { ok: true, item, mocked: true };
    }
  }

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error:
        response.status === 404
          ? "That response could not be found."
          : why(response.status),
    };
  }

  const item = (await response.json()) as ResponseItem;
  return { ok: true, item, mocked: false };
}

/**
 * The questions, for the labels.
 *
 * Answers are filed under each question's key and never under its label, which
 * is what lets a form be edited after somebody has answered it. The cost is
 * that a page of answers is unreadable on its own: the words have to be joined
 * back on from the form definition, here.
 */
export async function readFields(formId: string): Promise<FormField[]> {
  try {
    const response = await apiFetch(`/admin/forms/${formId}/draft`);
    if (!response.ok) {
      return [];
    }

    return ((await response.json()) as DraftView).draft.fields;
  } catch {
    return [];
  }
}

// ---------------------------------------------------------------------------
// Example data, until the API is there
// ---------------------------------------------------------------------------

/*
 * Everything below this line is scaffolding and is meant to be deleted.
 *
 * The responses endpoints are being built in parallel with this screen. Rather
 * than ship a page that cannot be looked at until they land, a 404 from them —
 * and only a 404, and only outside production — is answered with fabricated
 * submissions so the table, the panel, the paging and the empty state can all
 * be reviewed.
 *
 * The answers are generated from the form's real questions, so the shape is
 * the real shape: keyed by key, missing the questions older submissions were
 * never asked, carrying one key no question has any more. When the endpoints
 * land this block goes and nothing above it changes.
 */

/** Never in production. A missing endpoint there is a fault, not a fixture. */
const EXAMPLES = process.env.NODE_ENV !== "production";

/** Enough rows to need two pages, few enough to read. */
const EXAMPLE_ROWS = 14;
const EXAMPLE_PAGE = 8;

/** Fixed, so the same page renders the same way on the server and the client. */
const EXAMPLE_LATEST = Date.UTC(2026, 2, 14, 18, 40);

function exampleStamp(index: number): string {
  return new Date(EXAMPLE_LATEST - index * 5_400_000).toISOString();
}

async function examplePage(
  formId: string,
  cursor: string | null,
): Promise<ResponsePage> {
  const fields = await readFields(formId);
  const from = cursor === null ? 0 : Number(cursor.replace("example:", "")) || 0;
  const to = Math.min(from + EXAMPLE_PAGE, EXAMPLE_ROWS);

  const items: ResponseItem[] = [];
  for (let index = from; index < to; index += 1) {
    items.push(exampleRow(fields, index));
  }

  return {
    items,
    nextCursor: to < EXAMPLE_ROWS ? `example:${to}` : null,
  };
}

async function exampleItem(
  formId: string,
  responseId: string,
): Promise<ResponseItem | null> {
  const index = Number(responseId.replace("example-", ""));
  if (!Number.isInteger(index) || index < 0 || index >= EXAMPLE_ROWS) {
    return null;
  }

  const item = exampleRow(await readFields(formId), index);

  return item.resume
    ? {
        ...item,
        resume: {
          ...item.resume,
          // A link that works and is obviously not a resume, so nobody
          // reviewing this mistakes the fixture for a live download.
          url: "data:text/plain,Example%20resume",
        },
      }
    : item;
}

function exampleRow(fields: FormField[], index: number): ResponseItem {
  // The older half were submitted before the last two questions were added,
  // and while a question that has since been deleted was still being asked.
  const older = index >= 5;
  const asked = older ? fields.slice(0, Math.max(fields.length - 2, 0)) : fields;

  const answers: Record<string, unknown> = {};
  for (const field of asked) {
    const value = exampleAnswer(field, index);
    if (value !== undefined) {
      answers[field.key] = value;
    }
  }

  if (older) {
    answers.question_removed = "Answered a question the form no longer asks";
  }

  return {
    id: `example-${index}`,
    submittedAt: exampleStamp(index),
    formVersion: older ? 3 : 4,
    answers,
    resume:
      index % 3 === 0
        ? { filename: `resume-${index + 1}.pdf`, sizeBytes: 148_000 + index * 9_311 }
        : null,
  };
}

function exampleAnswer(field: FormField, index: number): unknown {
  // Every fourth submission leaves the optional questions blank, because a
  // table where every cell is filled is one that never shows what an
  // unanswered question looks like.
  if (!field.required && index % 4 === 3) {
    return undefined;
  }

  switch (field.type) {
    case "file":
      return undefined;

    case "consent":
      return true;

    case "number":
      return 18 + (index % 7);

    case "date":
      return exampleStamp(index).slice(0, 10);

    case "email":
      return `applicant${index + 1}@example.edu`;

    case "phone":
      return `+1 410 555 0${(100 + index).toString().slice(-3)}`;

    case "paragraph":
      return [
        `Answer ${index + 1}.`,
        "",
        "A longer answer, with a line break in it, so the table can show that it truncates and the panel can show that it does not.",
      ].join("\n");

    case "checkboxes":
      return field.options.slice(0, 1 + (index % 2)).map((option) => option.value);

    case "select":
    case "radio":
      // One row in five answers with an option that has since been deleted,
      // which has no label left and falls back to the stored value.
      return index % 5 === 0
        ? "option_removed"
        : field.options[index % Math.max(field.options.length, 1)]?.value ?? null;

    default:
      return `Example answer ${index + 1}`;
  }
}
