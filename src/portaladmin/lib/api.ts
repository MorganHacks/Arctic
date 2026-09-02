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

/**
 * Makes a change, and returns what to tell the person if it did not happen.
 *
 * Null means it worked. Anything else is a sentence to put on the screen —
 * the API's own, because the API is the one that knows whether the address was
 * taken, the team was wrong, or the permission does not exist. Inventing a
 * message here would mean maintaining a second, worse copy of that knowledge.
 */
export async function apiWrite(
  method: "POST" | "DELETE",
  path: string,
  body?: unknown,
): Promise<string | null> {
  let response: Response;

  try {
    response = await apiFetch(path, {
      method,
      ...(body === undefined
        ? {}
        : {
            body: JSON.stringify(body),
            headers: { "content-type": "application/json" },
          }),
    });
  } catch {
    return "The API could not be reached. Try again.";
  }

  if (response.ok) {
    return null;
  }

  // 403 is the gate answering, not a fault. Naming the missing permission is
  // what turns "it doesn't work" into a request an admin can act on.
  if (response.status === 403) {
    return "You do not have permission to do that.";
  }

  if (response.status === 401) {
    return "Your session has ended. Sign in again.";
  }

  try {
    const { error } = (await response.json()) as { error?: string };
    return error ?? "That did not work.";
  } catch {
    return "That did not work.";
  }
}

export type Person = { personId: string };

/** A row on the people list. */
export type Listed = {
  id: string;
  kind: string;
  email: string;
  revoked: boolean;
  teams: string[];
};

/** One person, with everything needed to explain what they can do. */
export type PersonDetail = {
  id: string;
  kind: string;
  email: string;
  revoked: boolean;
  revokedAt: string | null;
  teams: { slug: string; expiresAt: string | null }[];
  grants: { permission: string; expiresAt: string | null }[];
  effective: string[];
};

export type Team = { slug: string; name: string; permissions: string[] };

/**
 * One recorded change to what somebody may do.
 *
 * Ids and slugs, never an address — the table holds none, and this type is the
 * shape the screen sees, so a page built on it cannot show what was never
 * recorded. The audit screen resolves ids to addresses through
 * `/admin/people`, which is gated separately.
 *
 * `action` is a string rather than a union of the actions that exist today.
 * The database writes it, and a union here would make an action the triggers
 * started recording into a type error on the screen that most needs to show
 * it.
 */
export type AuditEntry = {
  id: number;
  occurredAt: string;
  action: string;
  /** Null where nobody was behind it — a seed, an import, a fix run by hand. */
  actorId: string | null;
  /** Null exactly when `subjectTeam` is set. */
  subjectId: string | null;
  /** Set instead of `subjectId` when a team's baseline changed. */
  subjectTeam: string | null;
  /** The team slug or permission that changed. */
  target: string | null;
  expiresAt: string | null;
  detail: Record<string, unknown>;
};

/**
 * Teams and the permissions that exist at all, both from the API.
 *
 * The catalogue is deliberately not a constant in this repo. Twenty-three
 * strings copied into TypeScript drift the moment one is added on the other
 * side, and the drift is silent: a permission the API enforces that no admin
 * can ever grant.
 */
export type Catalogue = {
  teams: Team[];
  permissions: { value: string; sensitive: boolean }[];
};

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

/**
 * What the signed-in person may do, so the screens can stop offering what they
 * cannot.
 *
 * Cosmetic only. Hiding a button is a courtesy; the API refuses the request
 * whether or not the button was there, and that refusal is the actual
 * boundary. Anything that treats this list as the gate has the model backwards.
 */
export async function currentPermissions(personId: string): Promise<Set<string>> {
  try {
    const response = await apiFetch(`/admin/people/${personId}`);
    if (!response.ok) {
      return new Set();
    }

    const { effective } = (await response.json()) as PersonDetail;
    return new Set(effective);
  } catch {
    return new Set();
  }
}
