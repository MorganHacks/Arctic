"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { eventsFetch } from "./api";

export type NewEventState = { error?: string };

/**
 * What a write came back with.
 *
 * Null error means it happened. Everything else is a sentence to put on the
 * screen, and the API's own wherever it gave one — it is the side that knows
 * whether the slug was taken, and a second copy of that knowledge over here
 * would be a worse one.
 */
export type WriteResult = { ok: boolean; error?: string };

/**
 * What a 404 from one of these endpoints means.
 *
 * The events endpoints were being built while this screen was, so for a while
 * either can answer 404. Told apart from every other failure on purpose: "not
 * there yet" is a different sentence from "it went wrong", and only one of
 * them is worth reporting to anybody.
 */
const NOT_SHIPPED = "Not available yet. Nothing changed.";

async function readFailure(response: Response): Promise<WriteResult> {
  if (response.status === 403) {
    return { ok: false, error: "You do not have permission to do that." };
  }

  if (response.status === 401) {
    return { ok: false, error: "Your session has ended. Sign in again." };
  }

  if (response.status === 404) {
    return { ok: false, error: NOT_SHIPPED };
  }

  const body = (await response.json().catch(() => ({}))) as { error?: string };
  return { ok: false, error: body.error ?? "That did not work." };
}

/**
 * Makes the event everything else hangs off.
 *
 * A slug and a name and nothing else, because in the week an event is created
 * that is genuinely all anybody knows. Asking for the dates here would mean
 * either inventing them or leaving the form half empty, and the dates screen
 * this lands on is where they belong anyway.
 */
export async function createEvent(
  _previous: NewEventState,
  form: FormData,
): Promise<NewEventState> {
  const slug = String(form.get("slug") ?? "").trim();
  const name = String(form.get("name") ?? "").trim();

  if (slug === "" || name === "") {
    return { error: "A slug and a name are both needed." };
  }

  let response: Response;

  try {
    response = await eventsFetch("/admin/events", {
      method: "POST",
      body: JSON.stringify({ slug, name }),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (!response.ok) {
    const failure = await readFailure(response);
    return { error: failure.error };
  }

  // The id if the answer carried one, so the person lands on the screen where
  // the dates go. Not required: an event exists either way, and a redirect
  // that cannot be worked out is not worth failing a creation over.
  const created = await response.json().catch(() => ({}));
  const id = idOf(created);

  revalidatePath("/events");

  // Outside the try, because redirect works by throwing and a catch above it
  // would swallow the navigation.
  redirect(id === null ? "/events" : `/events/${id}`);
}

/**
 * Everything about an event that can be changed after it is created.
 *
 * The name sits beside the dates rather than in a form of its own because it
 * is corrected in the same moment they are: somebody opens this screen after
 * the meeting that settled a date and fixes the typo they made in the name
 * three months ago while they are here.
 *
 * The slug is deliberately absent. It is what links, forms and campaign
 * segments refer to this event by, and a renamed identifier is a broken one.
 * The API does not accept it either, so this is the same rule stated twice
 * rather than a decision taken here.
 */
export type EventEdit = {
  name: string;
  startsAt: string | null;
  endsAt: string | null;
  registrationOpensAt: string | null;
  registrationClosesAt: string | null;
  decisionsAnnouncedAt: string | null;
  capacity: number | null;
};

/**
 * Writes the name, the dates and the capacity.
 *
 * Every field is sent on every save, including the ones that are null. Sending
 * only what changed would leave clearing a date indistinguishable from not
 * touching it, and going back to undecided is a thing that genuinely happens:
 * a date gets penciled in, the room falls through, and the honest state of the
 * field afterwards is empty rather than wrong.
 *
 * An empty name is left for the API to refuse rather than caught here. It is
 * the side that owns the sentence, and a second copy of the rule over here is
 * one that drifts the first time the API's wording changes.
 */
export async function saveEvent(
  eventId: string,
  edit: EventEdit,
): Promise<WriteResult> {
  let response: Response;

  try {
    response = await eventsFetch(`/admin/events/${eventId}`, {
      method: "PUT",
      body: JSON.stringify(edit),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { ok: false, error: "The API could not be reached. Try again." };
  }

  if (!response.ok) {
    return readFailure(response);
  }

  revalidatePath("/events");
  revalidatePath(`/events/${eventId}`);
  return { ok: true };
}

function idOf(body: unknown): string | null {
  if (typeof body !== "object" || body === null) {
    return null;
  }

  const record = body as Record<string, unknown>;
  if (typeof record.id === "string") {
    return record.id;
  }

  const nested = record.event;
  if (typeof nested === "object" && nested !== null) {
    const id = (nested as Record<string, unknown>).id;
    return typeof id === "string" ? id : null;
  }

  return null;
}
