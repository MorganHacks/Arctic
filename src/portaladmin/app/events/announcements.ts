import type { AnnouncementContent, AnnouncementResults } from "../../../../libs/ui/announcements";
import type { AnnouncementReactions } from "../../../../libs/ui/announcement-reactions";
import { apiFetch } from "@/lib/api";

/**
 * Everything the announcements panel asks the API for.
 *
 * Separate from `./api.ts` because that file carries a development mock for the
 * events endpoints, and these are real. One file per seam is easier to delete
 * than one file with two lifetimes in it.
 */

/** One notice, as an organizer sees it: retracted ones included. */
export type AdminAnnouncement = {
  id: string;
  body: string;
  postedAt: string;
  publishAt: string;
  scheduled: boolean;
  postedBy: string;
  retracted: boolean;
  retractedAt: string | null;
  content: AnnouncementContent | null;
  results: AnnouncementResults | null;
  reactions: AnnouncementReactions;
};

/**
 * A refusal is told apart from a failure because they are different sentences
 * and only one of them is anybody's fault. The same shape the events screens
 * use, for the same reason.
 */
export type AnnouncementsResult =
  | { state: "ok"; announcements: AdminAnnouncement[] }
  | { state: "forbidden" }
  | { state: "signed-out" }
  | { state: "failed" };

function outcome(status: number): "forbidden" | "signed-out" | "failed" {
  if (status === 401) return "signed-out";
  if (status === 403) return "forbidden";
  return "failed";
}

export async function listAnnouncements(
  eventId: string,
): Promise<AnnouncementsResult> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/events/${encodeURIComponent(eventId)}/announcements`,
    );
  } catch {
    return { state: "failed" };
  }

  if (!response.ok) {
    return { state: outcome(response.status) };
  }

  try {
    const body = (await response.json()) as {
      announcements?: AdminAnnouncement[];
    };
    return { state: "ok", announcements: body.announcements ?? [] };
  } catch {
    return { state: "failed" };
  }
}

/**
 * What a write came back with.
 *
 * The API's own sentence is carried through rather than replaced. It is the
 * side that knows why it refused — the body was empty, or too long, or the
 * event is gone — and a screen inventing its own wording for those would drift
 * from the rule that produced them.
 */
export type WriteResult = { ok: true } | { ok: false; error: string };

const UNREACHABLE = "The API could not be reached. Try again in a moment.";

async function said(response: Response, fallback: string): Promise<string> {
  try {
    const body = (await response.json()) as { error?: unknown };
    return typeof body.error === "string" ? body.error : fallback;
  } catch {
    return fallback;
  }
}

export async function postAnnouncement(
  eventId: string,
  body: string,
  content: AnnouncementContent | null = null,
  publishAt: string | null = null,
): Promise<WriteResult> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/events/${encodeURIComponent(eventId)}/announcements`,
      {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ body, content, publishAt }),
      },
    );
  } catch {
    return { ok: false, error: UNREACHABLE };
  }

  if (response.ok) {
    return { ok: true };
  }

  // COPY: needs sign-off.
  return {
    ok: false,
    error: await said(
      response,
      response.status === 403
        ? "You do not have permission to post announcements."
        : "That could not be posted. Try again in a moment.",
    ),
  };
}

export async function retractAnnouncement(id: string): Promise<WriteResult> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/announcements/${encodeURIComponent(id)}/retract`,
      { method: "POST" },
    );
  } catch {
    return { ok: false, error: UNREACHABLE };
  }

  if (response.ok) {
    return { ok: true };
  }

  // COPY: needs sign-off.
  return {
    ok: false,
    error: await said(
      response,
      "That could not be retracted. Reload and check whether it is still up.",
    ),
  };
}
