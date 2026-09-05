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
 * Calls the API as the signed-in applicant.
 *
 * The session cookie is forwarded by hand because a server component has no
 * browser attached to it. Nothing else is forwarded, and in particular no
 * person id: the API works out whose application this is from the session, and
 * anything this app passed along would be something it could be tricked into
 * passing wrong.
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
    // Never cached. Every page here renders one person's application, and a
    // cache that outlives a request is a cache that can show one applicant
    // another applicant's screen.
    cache: "no-store",
  });
}

/**
 * The applicant's own application, in the words the API is willing to say.
 *
 * There is deliberately no status code on this type. The API maps the internal
 * status to a sentence before it leaves atlas, and nothing on this side ever
 * sees the enum — which is what stops a screen here inventing its own mapping
 * and disagreeing with the one the team signed off.
 */
export type Application = {
  statusLabel: string;
  nextStep: string;
  receivedAt: string | null;
  profileEditable: boolean;
  profileLockedReason: string | null;
  profile: Profile;
  shirtSizes: string[];
};

export type Profile = {
  firstName: string | null;
  lastName: string | null;
  school: string | null;
  shirtSize: string | null;
  dietaryNeeds: string | null;
  accessibilityNeeds: string | null;
};

/**
 * A QR symbol as the API draws it: one character per module, '1' for dark.
 *
 * Modules rather than an image, so the size, the colour and the quiet zone are
 * decided by the screen showing it rather than baked into a string on the
 * server.
 */
export type QrSymbol = {
  size: number;
  rows: string[];
};

/**
 * The check-in screen, with or without a code on it.
 *
 * `code` is null for anybody the door would turn away, and the sentences are
 * written for that case too. Nothing here is a status: like everywhere else in
 * this app, the API sends the words and this side does not map anything.
 */
export type CheckInPass = {
  heading: string;
  explanation: string;
  hint: string | null;
  code: string | null;
  /** The same code in three groups of four, which is how it is read out. */
  display: string | null;
  qr: QrSymbol | null;
  checkedIn: boolean;
};

/** One line of mail history. Subject and outcome, never the body. */
export type Message = {
  id: string;
  subject: string;
  at: string;
  delivery: string;
};

/**
 * What every signed-in page needs, or null when there is no session.
 *
 * Null covers every reason equally — no cookie, expired, revoked, or the API
 * being unreachable. The pages treat all of them as "sign in again", which is
 * the only thing an applicant can do about any of them.
 */
export type Portal = { application: Application | null };

export async function currentPortal(): Promise<Portal | null> {
  try {
    const response = await apiFetch("/portal/me");
    if (!response.ok) {
      return null;
    }

    return (await response.json()) as Portal;
  } catch {
    return null;
  }
}

/**
 * The check-in code, or the reason there is not one yet.
 *
 * Null covers every reason the call failed, like the two above it, and the
 * page treats all of them as "sign in again" because that is the only thing an
 * applicant can do about any of them.
 */
export async function checkInPass(): Promise<CheckInPass | null> {
  try {
    const response = await apiFetch("/portal/check-in");
    if (!response.ok) {
      return null;
    }

    return (await response.json()) as CheckInPass;
  } catch {
    return null;
  }
}

export async function messageHistory(): Promise<Message[] | null> {
  try {
    const response = await apiFetch("/portal/messages");
    if (!response.ok) {
      return null;
    }

    const { messages } = (await response.json()) as { messages: Message[] };
    return messages;
  } catch {
    return null;
  }
}

/**
 * A date an applicant can read, in their own time zone.
 *
 * Rendered from the ISO instant on the client's clock would mismatch on
 * hydration, so this formats in UTC and says the day rather than the minute.
 * A day is the precision any of these dates actually carry.
 */
export function readableDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-US", {
    year: "numeric",
    month: "long",
    day: "numeric",
    timeZone: "UTC",
  });
}
