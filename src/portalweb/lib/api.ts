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
  rsvp: Rsvp;
  withdraw: Withdrawal;
  profileEditable: boolean;
  profileLockedReason: string | null;
  profile: Profile;
  shirtSizes: string[];
};

/**
 * Whether there is a spot to answer for, and by when.
 *
 * `open` is the API's answer to the same question the API asks itself before
 * accepting the write, which is why the screen reads it rather than working it
 * out from a status and a date. There is no status here to work it out from,
 * and that is deliberate.
 *
 * `deadline` is an ISO instant and null unless it may be shown. Rendering it is
 * this side's job because the zone is a display decision — see libs/ui/zone.ts,
 * which is where the event's zone lives.
 */
export type Rsvp = {
  open: boolean;
  deadline: string | null;
  closedReason: string | null;
};

/**
 * Whether the applicant may close their own application, and why not if they
 * may not.
 *
 * The same shape as `Rsvp` and for the same reason: `open` is the API's answer
 * to the question it asks itself before accepting the write, so the screen
 * cannot offer a button the endpoint would refuse — nor withhold one it would
 * accept, which is the failure nobody notices.
 *
 * `closedReason` is a whole sentence, chosen on the API side from the status
 * this app is never shown. It is rendered as it came: a second copy of that
 * reasoning over here would be the one that drifts, and it would drift towards
 * naming a decision the applicant has not been told yet.
 */
export type Withdrawal = {
  open: boolean;
  closedReason: string | null;
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

/**
 * One notice the team posted to everybody at the event.
 *
 * The one thing the API sends this app that is neither a sentence the codebase
 * chose nor a field to render — it is what an organizer typed, and it is shown
 * as it was typed. Everywhere else in this file the rule is that the API sends
 * the words so a screen cannot invent its own mapping; here the words are the
 * data, and the rule that replaces it is that this side never edits them. No
 * truncation, no "read more": a notice cut off mid-sentence is a schedule
 * change nobody can act on.
 *
 * There is no author on this type and no retraction state, because there is
 * neither on the wire. A notice is the team speaking rather than one named
 * organizer, and a retracted one simply is not in the response.
 */
export type Announcement = {
  id: string;
  body: string;
  at: string;
};

/**
 * The resume we are holding, as the applicant is allowed to see it.
 *
 * A name, a size and a date, and deliberately no way to fetch the bytes. The
 * organizers' side of the API mints a signed link because a reviewer has to
 * read a file they have never seen; the applicant is the person who uploaded
 * it and already has it. There is no storage key here either — see
 * `Redaction.SensitiveKeys` on the API side, which lists it as the one string
 * that turns "somebody has a CV" into "here it is".
 */
export type ResumeOnFile = {
  filename: string;
  size: number | null;
  uploadedAt: string | null;
};

/**
 * The resume screen: what is on file, and whether it may be changed.
 *
 * `started` and `editable` are both false for somebody who has not applied
 * yet, and the two need different sentences — so the API says which it is
 * rather than leaving this side to infer it from `lockedReason` being null.
 *
 * `maxBytes` and `accepts` come from the API because the API enforces them. A
 * page carrying its own copy of those numbers is a page that eventually
 * disagrees with the server about what will be accepted, and the person who
 * finds out is the one whose upload was refused after five minutes.
 */
export type ResumeScreen = {
  resume: ResumeOnFile | null;
  started: boolean;
  editable: boolean;
  lockedReason: string | null;
  maxBytes: number;
  accepts: string;
};

/**
 * What resume, if any, is on this applicant's application.
 *
 * Null covers every reason the call failed, like the reads above it, and the
 * page treats all of them as "sign in again" because that is the only thing an
 * applicant can do about any of them.
 */
export async function currentResume(): Promise<ResumeScreen | null> {
  try {
    const response = await apiFetch("/portal/resume");
    if (!response.ok) {
      return null;
    }

    return (await response.json()) as ResumeScreen;
  } catch {
    return null;
  }
}

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
 * What the team has posted for this applicant's event, newest first.
 *
 * Empty for somebody who has not applied, which is the API's answer rather
 * than a case handled here: the feed is scoped to the event of the reader's own
 * application, and no application means no event and therefore nothing.
 *
 * Null covers every reason the call failed, like the two above it, and the page
 * treats all of them as "sign in again".
 */
export async function announcements(): Promise<Announcement[] | null> {
  try {
    const response = await apiFetch("/portal/announcements");
    if (!response.ok) {
      return null;
    }

    const { announcements } = (await response.json()) as {
      announcements: Announcement[];
    };
    return announcements;
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
