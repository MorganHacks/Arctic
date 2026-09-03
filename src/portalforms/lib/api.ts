import { cookies } from "next/headers";

// Scaffolding. Goes with the block in loadForm below. See lib/preview.ts.
import { previewForm } from "./preview";

/**
 * Where the API lives as far as the server is concerned.
 *
 * The browser reaches it at this app's own origin, through the rewrite in
 * next.config.ts. Server components cannot use that — a rewrite is a browser
 * concern — so they call harbor directly.
 *
 * Read at build time, like the other portals: Next compiles rewrites into the
 * routes manifest during `next build`, so setting this only when starting the
 * server changes nothing.
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
 * The question types a form can ask. Spelled as the API spells them.
 *
 * `section` is the odd one out and deliberately still lives here rather than in
 * a list of its own. It arrives in the same `fields` array as everything else,
 * so keeping it in the same union is what makes the compiler point at every
 * place that assumed a field was a question — which is the whole of the risk in
 * adding it.
 *
 * A section is never answered. It has no control, takes no value, and is never
 * sent. It marks where one step of the form ends and the next begins.
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
 * One question, as an applicant is allowed to see it.
 *
 * Deliberately smaller than the stored field. Where an answer is kept is the
 * API's business, and the page has no use for it.
 */
export type Field = {
  key: string;
  type: FieldType;
  label: string;
  help: string | null;
  required: boolean;
  options: FieldOption[];
  minLength: number | null;
  maxLength: number | null;
  min: number | null;
  max: number | null;
};

/**
 * What state this page is in.
 *
 * A word rather than a status code, because none of these is an error and
 * every one of them is a page with something on it. `signIn` has something to
 * do — put in an address and wait for a link — and `ineligible` has nothing,
 * and the two must never be conflated: offering a sign-in box to somebody who
 * is already signed in is how a person requests four links to a form that will
 * not open for them either way.
 */
export type Access = "open" | "closed" | "signIn" | "ineligible";

/**
 * An answer we already hold.
 *
 * The same shapes the form posts back, because that is what it is: what
 * somebody told us last time, keyed by question.
 */
export type Prefill = string | string[] | boolean | number;

/**
 * A form behind its code.
 *
 * `fields` is absent whenever there is nothing to fill in — a closed form, or
 * one that requires a sign-in this reader has not done. That is the API being
 * careful rather than terse: questions attached to a page nobody can submit
 * invite a form rendered behind a banner somebody scrolls straight past.
 */
export type PublicForm = {
  code: string;
  name: string;
  kind: string;
  open: boolean;
  closesAt: string | null;

  /** Whether the link alone is enough. False for the application form, always. */
  requiresSignIn: boolean;
  access: Access;

  version?: number;
  fields?: Field[];

  /**
   * Who we have decided this is, from their record.
   *
   * Present only once they are signed in and eligible. The form never asks for
   * a name or an address on a form like this — that is the entire point of
   * signing in — so this is what the page prints instead.
   */
  you?: { name: string | null; email: string };

  /** What they have already told us, keyed by question. Editable. */
  prefill?: Record<string, Prefill>;

  /**
   * The questions they may not answer for themselves, keyed by question.
   *
   * Shown as fixed rather than hidden. A question that vanishes is one
   * somebody assumes was never asked; a question shown with its answer and no
   * control says what we hold and that this is not the place to change it.
   */
  fixed?: string[];
};

/** The cookie a signed-in form is read with. Set by /api/auth/consume. */
const SESSION_COOKIE = "mh_session";

/**
 * Hands the reader's session on to the API, when they have one.
 *
 * This page is rendered on the server and the server is a different client
 * from the browser: nothing in the browser's cookie jar is attached to a fetch
 * made here unless it is put there. Without this, a signed-in form would
 * render its signed-out state on first paint and only correct itself once
 * something in the browser asked again — which on campus wifi is a sign-in box
 * shown to somebody who is already signed in.
 *
 * Only the one cookie. Forwarding the whole header would send anything else
 * this origin happens to be holding to an API that has no business reading it.
 *
 * Reading a cookie makes this request dynamic, which it already was: the form
 * is fetched `no-store` because a form closes at a moment somebody chose and a
 * new version can be published while people are filling in the old one.
 */
async function sessionHeader(): Promise<Record<string, string>> {
  const session = (await cookies()).get(SESSION_COOKIE);
  return session ? { cookie: `${SESSION_COOKIE}=${session.value}` } : {};
}

/**
 * Resolves the code in the URL. Null when there is no form to show.
 *
 * Null covers a code nobody issued, a code with a typo in it, and a form whose
 * only version is still a draft. The API answers all three the same way on
 * purpose — telling them apart would be a way to find out which
 * seven-character codes are real — and so does this page.
 */
export async function loadForm(code: string): Promise<PublicForm | null> {
  /* ---- Scaffolding. Delete this block and the import with lib/preview.ts. --
   *
   * A made-up form with sections in it, so the multi-step page can be looked at
   * before the API can serve one. Returns null unless both of its locks are
   * open, and one of them is `NODE_ENV !== "production"`, so a shipped build
   * never gets past this line. See lib/preview.ts.
   */
  const preview = previewForm(code);
  if (preview) {
    return preview;
  }
  /* ---- End of the scaffolding. ------------------------------------------ */

  let response: Response;

  try {
    response = await fetch(
      `${apiOrigin}/api/forms/${encodeURIComponent(code)}`,
      {
        headers: { ...proxyHeader, ...(await sessionHeader()) },
        /*
         * Never cached.
         *
         * A form closes at a moment somebody chose, and a new version can be
         * published while people are filling in the old one. A cached copy
         * shows an applicant questions that are no longer the ones being
         * asked, and their answers are then stored against a version they
         * were never given.
         */
        cache: "no-store",
      },
    );
  } catch {
    // The API being unreachable is not "no such form". Thrown so the error
    // boundary shows a page that says to try again, rather than telling
    // somebody with a perfectly good link that it is wrong.
    throw new Error("The forms service could not be reached.");
  }

  if (response.status === 404) {
    return null;
  }

  if (!response.ok) {
    throw new Error(`The forms service answered ${response.status}.`);
  }

  return (await response.json()) as PublicForm;
}
