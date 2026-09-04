import { apiFetch } from "@/lib/api";
import type { EventRow } from "@/components/events/types";

/**
 * Every call these screens make, in one place.
 *
 * It exists so the mock below has exactly one seam to sit in. Nothing else in
 * this folder calls `apiFetch` directly, so there is one function to read to
 * know what this screen asks the API for.
 */
async function eventsFetch(path: string, init?: RequestInit): Promise<Response> {
  /* ------------------------------------------------------------------ *
   * BEGIN local mock. Delete this block and ./mock.ts together.
   *
   * The events endpoints are being built in a parallel session, so while this
   * screen was written they answered 404. This keeps a handful of events in
   * memory so the list, the create form, the edit form and the empty state can
   * all be worked on.
   *
   * Guarded twice: never outside development, and never without somebody
   * turning it on by hand with EVENTS_MOCK=1. Imported dynamically so it is
   * not in a production bundle at all.
   * ------------------------------------------------------------------ */
  if (process.env.NODE_ENV !== "production" && process.env.EVENTS_MOCK === "1") {
    const { mockEvents } = await import("./mock");
    const mocked = mockEvents(path, init);
    if (mocked !== null) {
      return mocked;
    }
  }
  /* END local mock ---------------------------------------------------- */

  return apiFetch(path, init);
}

/**
 * What reading the list came back with.
 *
 * A refusal is told apart from a failure because they are different sentences
 * and only one of them is anybody's fault. Everything else collapses into
 * `failed`: an organizer can do the same thing about a 500 and an unreachable
 * API, which is try again.
 */
export type EventsResult =
  | { state: "ok"; events: EventRow[] }
  | { state: "forbidden" }
  | { state: "signed-out" }
  | { state: "failed" };

export async function listEvents(): Promise<EventsResult> {
  let response: Response;

  try {
    response = await eventsFetch("/admin/events");
  } catch {
    return { state: "failed" };
  }

  if (response.status === 403) {
    return { state: "forbidden" };
  }

  if (response.status === 401) {
    return { state: "signed-out" };
  }

  if (!response.ok) {
    return { state: "failed" };
  }

  try {
    const body = (await response.json()) as { events?: unknown };
    return { state: "ok", events: readEvents(body.events) };
  } catch {
    return { state: "failed" };
  }
}

/**
 * The API's answer, read field by field.
 *
 * Defensive on purpose. A date this response does not carry reads as a date
 * nobody has set, which is the truthful thing for a screen whose whole subject
 * is fields that are usually empty — and it means a response shaped slightly
 * differently from what was expected renders a row rather than a stack trace.
 */
function readEvents(value: unknown): EventRow[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.flatMap((entry) => {
    if (typeof entry !== "object" || entry === null) {
      return [];
    }

    const row = entry as Record<string, unknown>;
    const id = text(row.id);
    if (id === null) {
      return [];
    }

    return [
      {
        id,
        slug: text(row.slug) ?? "",
        name: text(row.name) ?? "",
        startsAt: text(row.startsAt),
        endsAt: text(row.endsAt),
        registrationOpensAt: text(row.registrationOpensAt),
        registrationClosesAt: text(row.registrationClosesAt),
        decisionsAnnouncedAt: text(row.decisionsAnnouncedAt),
        capacity: typeof row.capacity === "number" ? row.capacity : null,
      },
    ];
  });
}

function text(value: unknown): string | null {
  return typeof value === "string" && value !== "" ? value : null;
}

export { eventsFetch };
