import { cookies } from "next/headers";

/**
 * Where the API lives as far as the server is concerned.
 *
 * The browser reaches it at this app's own origin, through the rewrite in
 * next.config.ts. Server components cannot use that — a rewrite is a browser
 * concern — so they call harbor directly.
 */
/*
 * Harbor, not atlas.
 *
 * Every request here is /api/something, and stripping that prefix is harbor's
 * job -- atlas serves /forms, not /api/forms. Pointed straight at atlas every
 * call 404s, which surfaces as a form that says it does not exist and a
 * console that redirects to sign-in forever, with nothing in any log saying
 * why. The old default was atlas, so it could never have worked.
 */
const apiOrigin = process.env.API_ORIGIN ?? "http://localhost:5050";

/*
 * Proof to harbor that this request came through us.
 *
 * Harbor has a public hostname, so a forwarded client address is only
 * believed when it arrives with this. Without it the caller is bucketed on
 * the connection, which for us is Vercel -- one bucket for everybody. That is
 * a worse rate limit; sending a spoofable header was no rate limit at all.
 */
const proxySecret = process.env.PROXY_SHARED_SECRET ?? "";

/** The header harbor and atlas check. Empty secret sends nothing. */
const proxyHeader: Record<string, string> = proxySecret
  ? { "x-mh-proxy": proxySecret }
  : {};


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
      ...proxyHeader,
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

/**
 * The question types the form can ask, and the one type that is not a question.
 *
 * The names are the enum's, camel-cased on the way out by atlas. Not a copy of
 * a list that lives elsewhere in TypeScript — the API is the only place these
 * exist, and a field whose type does not round-trip is one that renders as an
 * empty box in front of applicants.
 *
 * `section` is a page break carrying the heading of the page it opens. It sits
 * in the same array as the questions and is never answered, so every screen
 * that walks the fields expecting an answer has to step over it.
 */
export type FieldType =
  | "shortText"
  | "paragraph"
  | "email"
  | "phone"
  | "number"
  | "date"
  | "select"
  | "radio"
  | "checkboxes"
  | "consent"
  | "file"
  | "section";

export type FieldOption = { value: string; label: string };

/**
 * One question, exactly as it is stored — or one page break.
 *
 * Sent back whole on every save, including the properties the builder never
 * shows — `storage`, `column`, the length bounds. Dropping what this screen
 * does not edit would mean the first autosave quietly rewriting where a
 * question's answers are filed.
 *
 * On a `section`, `label` is the page's heading and `help` is its description.
 * `required` and `options` are not ignored there but refused: the API will not
 * publish a page break that sets either, which is why the editor does not offer
 * them.
 */
export type FormField = {
  key: string;
  type: FieldType;
  label: string;
  help?: string | null;
  required: boolean;
  options: FieldOption[];
  storage: "column" | "responses";
  column?: string | null;
  minLength?: number | null;
  maxLength?: number | null;
  min?: number | null;
  max?: number | null;
};

/** Something wrong with a form, and which question it belongs to. */
export type FormProblem = { message: string; fieldKey: string | null };

export type EventSummary = {
  id: string;
  slug: string;
  name: string;
  startsAt: string | null;
};

export type FormSummary = {
  id: string;
  eventId: string;
  code: string;
  name: string;
  kind: string;
  closesAt: string | null;

  /**
   * Whether this form is for people we already have on file.
   *
   * Always false on the application form, and the API refuses to make it
   * anything else: applying is how somebody gets an account, so a gate there
   * would make applying impossible.
   */
  requiresSignIn: boolean;

  /** Which applicants may open it. Empty unless it requires sign-in. */
  eligibleStatuses: string[];
};

/** A row on the forms list. */
export type FormRow = {
  id: string;
  code: string;
  name: string;
  kind: string;
  closesAt: string | null;
  requiresSignIn: boolean;
  eligibleStatuses: string[];
  published: boolean;
  publishedVersion: number | null;
  questions: number | null;
};

export type FormsView = {
  events: EventSummary[];
  chosen: EventSummary | null;
  forms: FormRow[];
};

/** Everything the builder needs to draw itself, in one response. */
export type DraftView = {
  form: FormSummary;
  draft: { id: string; version: number; fields: FormField[] };
  published: { version: number; publishedAt: string | null } | null;

  /**
   * Every application status an audience can be built from, in lifecycle
   * order.
   *
   * From the server rather than a copy over here, so a status added to the
   * lifecycle appears in the builder without anybody remembering to add it
   * twice.
   */
  statuses: string[];
};

export type VersionRow = {
  version: number;
  status: string;
  questions: number;
  createdAt: string;
  publishedAt: string | null;
};

/**
 * Who is signed in, and what they may do.
 *
 * The permissions come from `/auth/me` rather than from this person's own
 * record. Reading that record needs `people.view`, and somebody on the
 * registration team holds `forms.manage` without it — so asking the wrong
 * endpoint got an empty set back and hid every button they were entitled to.
 */
export type Person = { personId: string; permissions: Set<string> };

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
 *
 * The permission set comes back with it, so no screen needs a second round
 * trip to work out what to offer.
 *
 * That set is cosmetic. Hiding a button is a courtesy; the API refuses the
 * request whether or not the button was there, and that refusal is the actual
 * boundary. Anything that treats this set as the gate has the model backwards.
 */
export async function currentPerson(): Promise<Person | null> {
  try {
    const response = await apiFetch("/auth/me");
    if (!response.ok) {
      return null;
    }

    const { personId, permissions } = (await response.json()) as {
      personId: string;
      permissions: string[];
    };

    return { personId, permissions: new Set(permissions) };
  } catch {
    return null;
  }
}
