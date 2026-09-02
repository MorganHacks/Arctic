"use server";

import { revalidatePath } from "next/cache";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { apiFetch } from "@/lib/api";

/**
 * What a form got back. An empty object is the state before anything was
 * submitted, which is why every field is optional rather than nullable.
 */
export type FormState = {
  /** Something went wrong, said in a way the person can act on. */
  error?: string;

  /** The write happened. */
  done?: boolean;

  /**
   * Something to say that is not an error.
   *
   * Separate from <c>error</c> because the sign-in confirmation is neither a
   * success nor a failure — it is the one sentence said either way, and
   * putting it in the error slot would make it a branch somebody later
   * "fixes" into two.
   */
  message?: string;
};

function text(form: FormData, field: string): string {
  const value = form.get(field);
  return typeof value === "string" ? value.trim() : "";
}

/**
 * The one sentence this page ever says about a sign-in request.
 *
 * Said whether or not the address belongs to anybody. "No account found" for
 * one address and "check your inbox" for another turns this form into a lookup
 * service for who applied to the hackathon — which is the whole reason the API
 * answers identically too, and it would be undone by a screen that branched on
 * the answer.
 */
const sent =
  "If that address has an account, a sign-in link is on its way. It expires "
  + "in 15 minutes and can only be used once.";

/**
 * Asks for a sign-in link.
 *
 * Returns the same confirmation for a known address, an unknown one, and a
 * throttled one, because the API returns the same thing for all three and the
 * point of that would be lost here otherwise.
 *
 * The single exception is the API being unreachable, and it is not a leak: a
 * gateway that is down is down for every address equally, and telling somebody
 * "your link is coming" when nothing was queued sends them to wait on an email
 * that will never arrive.
 *
 * KNOWN LIMITATION, and it belongs to the gateway rather than to this file.
 * This request is made by the server, so harbor sees this app's address as the
 * client and its per-IP limiter on /api/auth/* — ten in a quarter of an hour —
 * puts every applicant in one bucket. Atlas's per-address limit still holds,
 * so nobody can be mailed repeatedly, but the eleventh person to sign in
 * during registration week would be throttled for somebody else's requests.
 * The fix is harbor trusting a forwarded client address, which is the TODO
 * already sitting in its Program.cs, not a change here.
 */
export async function requestLink(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  const email = text(form, "email");
  if (email === "") {
    return { error: "Enter the email address you applied with." };
  }

  try {
    const response = await apiFetch("/auth/magic-link", {
      method: "POST",
      body: JSON.stringify({ email }),
      headers: { "content-type": "application/json" },
    });

    if (response.status >= 500) {
      return { error: "We could not send that just now. Try again in a minute." };
    }
  } catch {
    return { error: "We could not send that just now. Try again in a minute." };
  }

  return { done: true, message: sent };
}

/**
 * Saves the six fields an applicant owns.
 *
 * Every field is submitted every time and the API replaces all six, so
 * clearing a box clears the answer. That is deliberate: somebody who listed a
 * dietary need last year and no longer has one has to be able to say so, and a
 * form where blank means "leave it alone" gives them no way to.
 *
 * The API decides whether the save is allowed and returns the sentence
 * explaining a refusal. That sentence is shown as it came, because the API is
 * the side that knows why — writing a second copy of that reasoning here would
 * mean maintaining a worse one.
 */
export async function saveProfile(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  let response: Response;

  try {
    response = await apiFetch("/portal/profile", {
      method: "PATCH",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        firstName: text(form, "firstName"),
        lastName: text(form, "lastName"),
        school: text(form, "school"),
        shirtSize: text(form, "shirtSize"),
        dietaryNeeds: text(form, "dietaryNeeds"),
        accessibilityNeeds: text(form, "accessibilityNeeds"),
      }),
    });
  } catch {
    return { error: "We could not save that just now. Try again in a minute." };
  }

  if (response.status === 401) {
    redirect("/portal/sign-in");
  }

  if (!response.ok) {
    const { error } = (await response.json().catch(() => ({}))) as {
      error?: string;
    };
    return { error: error ?? "That could not be saved." };
  }

  // Both screens read the same application, and the status line on /portal
  // shows the name that was just changed.
  revalidatePath("/portal/profile");
  revalidatePath("/portal");
  return { done: true };
}

/**
 * Ends the session, here and in the database.
 *
 * The API revokes the row rather than only clearing the cookie, so signing out
 * on a shared library machine actually ends the session instead of leaving a
 * live one behind that a restored cookie could pick back up.
 */
export async function signOut(): Promise<void> {
  try {
    await apiFetch("/auth/logout", { method: "POST" });
  } catch {
    // Deliberately ignored. If the API cannot be reached there is nothing to
    // do about it here, and leaving somebody stuck on a signed-in-looking
    // screen is worse than sending them to sign-in with a session that will
    // be refused on its next use anyway.
  }

  // The cookie has to be cleared on this side too, and this is the half that
  // is easy to miss: the API's own Set-Cookie went to this server's fetch
  // response, not to the browser. Without it the session row is revoked but
  // the browser keeps presenting a dead cookie, which looks to the person like
  // signing out did nothing.
  (await cookies()).delete("mh_session");

  // Outside the try on purpose: redirect works by throwing, so it must never
  // sit where a catch could swallow it.
  redirect("/portal/sign-in");
}
