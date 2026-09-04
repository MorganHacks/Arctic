"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { apiFetch, type FormField, type FormProblem } from "@/lib/api";

export type NewFormState = { error?: string };

/**
 * What a save or a publish came back with.
 *
 * `problems` is the list the API produced, each one carrying the key of the
 * question it belongs to so the builder can put it under that question rather
 * than in a pile at the top. An empty list is not the same as a missing one:
 * empty means "checked, nothing wrong", missing means the request never got
 * far enough to check.
 */
export type SaveResult = {
  ok: boolean;
  error?: string;
  problems: FormProblem[];
};

type ApiFailure = { error?: string; problems?: FormProblem[] };

/**
 * What a 404 from one of the newer endpoints means.
 *
 * Unpublish and the deadline are being built on the API side while this screen
 * is being built on ours, so for a while either can answer 404. Told apart from
 * every other failure on purpose: "not there yet" is a different sentence from
 * "it went wrong", and only one of them is worth reporting to anybody. The rest
 * of the screen carries on working either way.
 */
const NOT_SHIPPED = "Not available yet. Nothing changed.";

async function readFailure(response: Response): Promise<SaveResult> {
  // 403 is the gate answering, not a fault, and naming the permission is what
  // turns "it doesn't work" into a request an admin can act on.
  if (response.status === 403) {
    return {
      ok: false,
      error: "You do not have forms.manage. Ask an admin.",
      problems: [],
    };
  }

  if (response.status === 401) {
    return { ok: false, error: "Your session has ended. Sign in again.", problems: [] };
  }

  const body = (await response.json().catch(() => ({}))) as ApiFailure;
  return {
    ok: false,
    error: body.error ?? "That did not work.",
    problems: body.problems ?? [],
  };
}

/**
 * Writes the draft's questions.
 *
 * Deliberately does not revalidate. This is called from a debounce as somebody
 * types, and revalidating would re-render the server component that holds the
 * builder's initial state — pulling the questions out from under the cursor
 * mid-sentence. The page is refreshed on publish, which is the only moment the
 * server knows something the browser does not.
 */
export async function saveDraft(
  formId: string,
  fields: FormField[],
): Promise<SaveResult> {
  let response: Response;

  try {
    response = await apiFetch(`/admin/forms/${formId}/draft`, {
      method: "PUT",
      body: JSON.stringify({ fields }),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { ok: false, error: "The API could not be reached.", problems: [] };
  }

  if (!response.ok) {
    return readFailure(response);
  }

  const { problems } = (await response.json()) as { problems: FormProblem[] };
  return { ok: true, problems };
}

/**
 * Makes the draft the live form.
 *
 * The refusal is the interesting path. Every problem comes back at once,
 * because one at a time turns fixing a form into a guessing game where each
 * fix reveals the next complaint.
 */
export async function publishForm(formId: string): Promise<SaveResult> {
  let response: Response;

  try {
    response = await apiFetch(`/admin/forms/${formId}/publish`, { method: "POST" });
  } catch {
    return { ok: false, error: "The API could not be reached.", problems: [] };
  }

  if (!response.ok) {
    return readFailure(response);
  }

  revalidatePath(`/forms/${formId}`);
  revalidatePath("/forms");
  return { ok: true, problems: [] };
}

/**
 * Sets who a form is for.
 *
 * Both halves in one call, because they are one decision. A gate with nobody
 * behind it is a form nobody can open, and the API refuses that combination
 * rather than storing a form whose audience has to be guessed at.
 *
 * Revalidated, unlike the draft save above. This is a deliberate press rather
 * than a debounce as somebody types, and what it changes — who can open the
 * form — is shown in the header and on the list.
 */
export async function saveAudience(
  formId: string,
  requiresSignIn: boolean,
  eligibleStatuses: string[],
): Promise<SaveResult> {
  let response: Response;

  try {
    response = await apiFetch(`/admin/forms/${formId}/audience`, {
      method: "PUT",
      body: JSON.stringify({ requiresSignIn, eligibleStatuses }),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { ok: false, error: "The API could not be reached.", problems: [] };
  }

  if (!response.ok) {
    return readFailure(response);
  }

  revalidatePath(`/forms/${formId}`);
  revalidatePath("/forms");
  return { ok: true, problems: [] };
}

/**
 * Creates a form and goes straight into it.
 *
 * The list is not where a form gets built, and a new one has nothing on it
 * worth looking at from outside, so there is nothing to go back to.
 */
export async function createForm(
  _previous: NewFormState,
  form: FormData,
): Promise<NewFormState> {
  const value = (field: string) => {
    const raw = form.get(field);
    return typeof raw === "string" ? raw.trim() : "";
  };

  const name = value("name");
  if (name === "") {
    return { error: "A form needs a name." };
  }

  const eventId = value("eventId");
  const response = await apiFetch(
    `/admin/forms${eventId === "" ? "" : `?eventId=${encodeURIComponent(eventId)}`}`,
    {
      method: "POST",
      body: JSON.stringify({ name, kind: value("kind") || "survey" }),
      headers: { "content-type": "application/json" },
    },
  );

  if (!response.ok) {
    const { error } = await readFailure(response);
    return { error };
  }

  const { id } = (await response.json()) as { id: string };

  revalidatePath("/forms");
  // Outside the checks above on purpose: redirect works by throwing, so it
  // must never sit where a catch could swallow it.
  redirect(`/forms/${id}`);
}

/**
 * Takes a published form back to draft.
 *
 * The one irreversible control on this screen, and irreversible in the way that
 * matters rather than the way that sounds bad: the code printed on a flyer
 * stops resolving the moment this returns, and nobody holding that flyer finds
 * out except by trying. Publishing again brings the link back; it does not
 * bring back the afternoon in which nobody could answer.
 *
 * Answers are not touched, and the screen says so. The fear this control
 * provokes is "have I just deleted four hundred applications", and leaving that
 * unanswered is how a control that should be used stops being used.
 */
export async function unpublishForm(formId: string): Promise<SaveResult> {
  let response: Response;

  try {
    response = await apiFetch(`/admin/forms/${formId}/unpublish`, { method: "POST" });
  } catch {
    return { ok: false, error: "The API could not be reached.", problems: [] };
  }

  if (response.status === 404) {
    return { ok: false, error: NOT_SHIPPED, problems: [] };
  }

  if (!response.ok) {
    return readFailure(response);
  }

  revalidatePath(`/forms/${formId}`);
  revalidatePath(`/forms/${formId}/responses`);
  revalidatePath("/forms");
  return { ok: true, problems: [] };
}

/**
 * Sets or clears when a form stops accepting answers.
 *
 * `closesAt` arrives already resolved to an instant, from the wall-clock time
 * somebody typed against the event's zone. That resolution happens in the
 * browser and deliberately not here: a server action runs in whichever region
 * took the request, so anything on this side that consulted a local clock would
 * move the deadline depending on where the request landed.
 *
 * Null clears it. A form with no deadline is open until somebody unpublishes
 * it, which is not the same as a form that closed at the epoch.
 */
export async function scheduleForm(
  formId: string,
  closesAt: string | null,
): Promise<SaveResult> {
  let response: Response;

  try {
    response = await apiFetch(`/admin/forms/${formId}/schedule`, {
      method: "PUT",
      body: JSON.stringify({ closesAt }),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { ok: false, error: "The API could not be reached.", problems: [] };
  }

  if (response.status === 404) {
    return { ok: false, error: NOT_SHIPPED, problems: [] };
  }

  if (!response.ok) {
    return readFailure(response);
  }

  revalidatePath(`/forms/${formId}`);
  revalidatePath(`/forms/${formId}/responses`);
  revalidatePath("/forms");
  return { ok: true, problems: [] };
}
