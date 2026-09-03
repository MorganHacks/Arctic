"use server";

import { revalidatePath } from "next/cache";
import { apiFetch } from "@/lib/api";
import type { PageResult } from "@/components/applicants/types";
import { readPage, type Filter } from "./api";

/**
 * The three things the browser asks for after the page has loaded.
 *
 * Actions rather than route handlers, so the API's address, its paging
 * parameters and what its failures mean all stay on the server.
 *
 * All three are reachable by anybody with a session, as every action is. That
 * is not the gate — the API refuses on `applications.view`,
 * `applications.decide` and `applications.note` whoever asks, and these
 * forward its refusal rather than deciding anything themselves.
 */

/**
 * What a form got back. An empty object is the state before anything was
 * submitted, which is why `error` is optional rather than nullable.
 */
export type FormState = { error?: string };

/**
 * The next page of applicants. Never the first: that one arrives rendered.
 *
 * Deliberately does not revalidate. It answers a question about data already
 * on screen, and re-rendering the page would throw away every page somebody
 * has loaded to add one.
 */
export async function loadApplicants(
  filter: Filter,
  cursor: string,
): Promise<PageResult> {
  const read = await readPage(filter, cursor);

  return read.ok
    ? { ok: true, page: read.page }
    : { ok: false, error: read.error };
}

function text(form: FormData, field: string): string {
  const value = form.get(field);
  return typeof value === "string" ? value.trim() : "";
}

/**
 * Reads the API's own refusal, or says something true when it did not answer.
 *
 * The API's sentence rather than one invented here, because the API is the
 * one that knows whether the move was illegal, the applicant was gone, or the
 * reason was too long. A second copy of that knowledge over here would drift.
 */
async function refusal(response: Response, fallback: string): Promise<string> {
  if (response.status === 401) {
    return "Your session has ended. Sign in again.";
  }

  try {
    const { error } = (await response.json()) as { error?: string };
    return error ?? fallback;
  } catch {
    return fallback;
  }
}

/**
 * Moves an applicant to a new status.
 *
 * Whether the move is legal is the API's decision and not this form's. The
 * screen offers only what the record said was allowed, which is a courtesy;
 * two reviewers on the same applicant means the record can be out of date by
 * the time the button is pressed, and the 409 that comes back then is the
 * system working.
 */
export async function changeStatus(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  const id = text(form, "id");
  const status = text(form, "status");

  if (status === "") {
    return { error: "Pick a status." };
  }

  let response: Response;
  try {
    response = await apiFetch(`/admin/applicants/${encodeURIComponent(id)}/status`, {
      method: "POST",
      body: JSON.stringify({ status, reason: text(form, "reason") || null }),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (response.status === 403) {
    return { error: "You do not have applications.decide. Ask an admin." };
  }

  if (!response.ok) {
    return { error: await refusal(response, "That status could not be changed.") };
  }

  // The record is re-read rather than patched in place. The status is not the
  // only thing that moved: the history has a new row on it, the lifecycle
  // timestamps were stamped by the database, and what the applicant can do
  // next is different.
  revalidatePath(`/applicants/${id}`);
  revalidatePath("/applicants");
  return {};
}

/** Adds an internal note. Never shown to the applicant. */
export async function addNote(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  const id = text(form, "id");
  const body = text(form, "body");

  if (body === "") {
    return { error: "A note cannot be empty." };
  }

  let response: Response;
  try {
    response = await apiFetch(`/admin/applicants/${encodeURIComponent(id)}/notes`, {
      method: "POST",
      body: JSON.stringify({ body }),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (response.status === 403) {
    return { error: "You do not have applications.note. Ask an admin." };
  }

  if (!response.ok) {
    return { error: await refusal(response, "That note could not be saved.") };
  }

  revalidatePath(`/applicants/${id}`);
  return {};
}
