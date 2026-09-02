"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { apiFetch, apiWrite } from "@/lib/api";

/**
 * What a form got back. An empty object is the state before anything was
 * submitted, which is why `error` is optional rather than nullable.
 */
export type FormState = { error?: string };

/**
 * Turns a date input into the instant access should end.
 *
 * `<input type="date">` gives a bare day, and a bare day is ambiguous in a way
 * that matters here: "expires on the 15th" from an organizer means through the
 * end of the 15th, not at midnight when it starts. Read as the start of the
 * day it would cut a judge off before the event they were added for.
 *
 * UTC, because the API compares against UTC. That makes the boundary a few
 * hours off local midnight, which is the wrong thing to be precise about — the
 * grants this dates are for run for days, not minutes.
 */
function endOfDay(value: FormDataEntryValue | null): string | undefined {
  const day = typeof value === "string" ? value.trim() : "";
  if (day === "") {
    return undefined;
  }

  const parsed = new Date(`${day}T23:59:59.999Z`);
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.toISOString();
}

function text(form: FormData, field: string): string {
  const value = form.get(field);
  return typeof value === "string" ? value.trim() : "";
}

/**
 * Adds an address to the organizer allowlist.
 *
 * On success it goes straight to the new person's page rather than back to the
 * list. They land with no permissions at all — that is the model working — so
 * the next thing somebody has to do is put them on a team, and the list is not
 * where that happens.
 */
export async function addOrganizer(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  const email = text(form, "email");
  if (email === "") {
    return { error: "An email address is required." };
  }

  const response = await apiFetch("/admin/people", {
    method: "POST",
    body: JSON.stringify({ email }),
    headers: { "content-type": "application/json" },
  });

  if (!response.ok) {
    if (response.status === 403) {
      return { error: "You do not have people.manage_teams. Ask an admin." };
    }

    const { error } = (await response.json().catch(() => ({}))) as {
      error?: string;
    };
    return { error: error ?? "That organizer could not be added." };
  }

  const { id } = (await response.json()) as { id: string };

  revalidatePath("/people");
  // Outside the checks above on purpose: redirect works by throwing, so it
  // must never sit where a catch could swallow it.
  redirect(`/people/${id}`);
}

export async function joinTeam(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  const id = text(form, "id");
  const slug = text(form, "slug");

  if (slug === "") {
    return { error: "Pick a team." };
  }

  const error = await apiWrite("POST", `/admin/people/${id}/teams`, {
    slug,
    expiresAt: endOfDay(form.get("expiresAt")) ?? null,
  });

  if (error) {
    return { error };
  }

  revalidatePath(`/people/${id}`);
  revalidatePath("/people");
  return {};
}

export async function leaveTeam(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  const id = text(form, "id");
  const slug = text(form, "slug");

  const error = await apiWrite(
    "DELETE",
    `/admin/people/${id}/teams/${encodeURIComponent(slug)}`,
  );

  if (error) {
    return { error };
  }

  revalidatePath(`/people/${id}`);
  revalidatePath("/people");
  return {};
}

export async function grant(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  const id = text(form, "id");
  const permission = text(form, "permission");

  if (permission === "") {
    return { error: "Pick a permission." };
  }

  const error = await apiWrite("POST", `/admin/people/${id}/grants`, {
    permission,
    expiresAt: endOfDay(form.get("expiresAt")) ?? null,
  });

  if (error) {
    return { error };
  }

  revalidatePath(`/people/${id}`);
  return {};
}

export async function ungrant(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  const id = text(form, "id");
  const permission = text(form, "permission");

  // Encoded because permissions carry a dot, and an unencoded one in the last
  // path segment is the shape proxies and servers like to read as a file
  // extension.
  const error = await apiWrite(
    "DELETE",
    `/admin/people/${id}/grants/${encodeURIComponent(permission)}`,
  );

  if (error) {
    return { error };
  }

  revalidatePath(`/people/${id}`);
  return {};
}

/**
 * Takes someone off the allowlist and ends every session they hold.
 *
 * Both halves happen in one transaction inside the API, which is the only
 * arrangement that means anything: setting the flag alone leaves an open
 * laptop working, and cutting sessions alone lets the next sign-in restore
 * them.
 */
export async function revokePerson(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  const id = text(form, "id");

  const error = await apiWrite("POST", `/admin/people/${id}/revoke`);
  if (error) {
    return { error };
  }

  revalidatePath(`/people/${id}`);
  revalidatePath("/people");
  return {};
}
