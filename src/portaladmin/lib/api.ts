import { cookies } from "next/headers";

/**
 * Where the API lives as far as the server is concerned.
 *
 * The browser reaches it at this app's own origin, through the rewrite in
 * next.config.ts. Server components cannot use that — a rewrite is a browser
 * concern — so they call harbor directly.
 */
const apiOrigin = process.env.API_ORIGIN ?? "http://localhost:5080";

/**
 * Calls the API as the signed-in person.
 *
 * The session cookie is forwarded by hand because a server component has no
 * browser attached to it. Nothing else is forwarded: the API decides what this
 * person may do from the session alone, and passing anything else along would
 * invite trusting it.
 */
export async function apiFetch(
  path: string,
  init: RequestInit = {},
): Promise<Response> {
  const session = (await cookies()).get("mh_session");

  return fetch(`${apiOrigin}/api${path}`, {
    ...init,
    headers: {
      ...init.headers,
      ...(session ? { cookie: `mh_session=${session.value}` } : {}),
    },
    // Never cached. Every page here renders somebody's data, and a cache that
    // outlives a request is a cache that can show one organizer another
    // organizer's view.
    cache: "no-store",
  });
}

export type Person = { personId: string };

/**
 * Who is signed in, or null.
 *
 * Null covers every reason equally — no cookie, expired, revoked, or the API
 * being unreachable. The screens treat all of them as "sign in again", which
 * is the only thing a person can do about any of them.
 */
export async function currentPerson(): Promise<Person | null> {
  try {
    const response = await apiFetch("/auth/me");
    if (!response.ok) {
      return null;
    }

    return (await response.json()) as Person;
  } catch {
    return null;
  }
}
