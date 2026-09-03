import { apiFetch } from "@/lib/api";
import type {
  Applicant,
  ApplicantPage,
  ApplicantsView,
  Status,
} from "@/components/applicants/types";

/**
 * Reading applicants.
 *
 * Server-side only. Everything the browser needs goes through the actions in
 * this folder, so no component ever holds a URL to the API or decides what a
 * failure means.
 */

/**
 * How many applicants come back at a time.
 *
 * About two screens of rows. Small enough that the first page is up quickly on
 * the morning registration closes, large enough that reading several hundred
 * is a handful of clicks rather than a chore.
 */
const PAGE = 50;

export type ViewRead =
  | { ok: true; view: ApplicantsView }
  | { ok: false; status: number; error: string };

export type PageRead =
  | { ok: true; page: ApplicantPage }
  | { ok: false; status: number; error: string };

export type ApplicantRead =
  | { ok: true; applicant: Applicant }
  | { ok: false; status: number; error: string };

/** What the list is filtered by. All of it lives in the URL. */
export type Filter = {
  event?: string;
  q?: string;
  status?: Status[];
};

/**
 * What to say about a request that did not work.
 *
 * 403 names the permission, for the same reason the rest of this console does:
 * it turns "it doesn't work" into a request an admin can act on.
 */
function why(status: number, subject: string): string {
  if (status === 403) {
    return "You do not have applications.view. Ask an admin.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return `${subject} could not be loaded.`;
}

function query(filter: Filter, cursor: string | null): URLSearchParams {
  const params = new URLSearchParams({ limit: String(PAGE) });

  if (filter.event) {
    params.set("eventId", filter.event);
  }

  if (filter.q) {
    params.set("q", filter.q);
  }

  // Repeated rather than joined. One filter with several values is what the
  // API takes, and a comma-separated list would need both sides to agree on
  // what to do with a comma.
  for (const status of filter.status ?? []) {
    params.append("status", status);
  }

  if (cursor !== null && cursor !== "") {
    params.set("cursor", cursor);
  }

  return params;
}

/**
 * The first page, with the events and the counts that go around it.
 *
 * All three in one response because the screen needs all three to draw itself,
 * and three round trips to fill in a picker, a filter bar and a table is a
 * waterfall for no benefit.
 */
export async function readView(filter: Filter): Promise<ViewRead> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/applicants?${query(filter, null)}`);
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error:
        response.status === 400
          ? "That filter is not one of ours."
          : why(response.status, "Applicants"),
    };
  }

  return { ok: true, view: (await response.json()) as ApplicantsView };
}

/** One more page of the same list. */
export async function readPage(
  filter: Filter,
  cursor: string,
): Promise<PageRead> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/applicants?${query(filter, cursor)}`);
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error: why(response.status, "Applicants"),
    };
  }

  const view = (await response.json()) as ApplicantsView;
  return { ok: true, page: { items: view.items, nextCursor: view.nextCursor } };
}

/**
 * One applicant, with a fresh resume link.
 *
 * Read on every visit rather than cached, because the link it carries is
 * signed and lives about five minutes — and because a decision is being made
 * from what this says, so a stale status would be a decision made against a
 * record somebody else has already changed.
 */
export async function readApplicant(id: string): Promise<ApplicantRead> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/applicants/${encodeURIComponent(id)}`);
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error:
        response.status === 404
          ? "That applicant could not be found."
          : why(response.status, "That applicant"),
    };
  }

  return { ok: true, applicant: (await response.json()) as Applicant };
}
