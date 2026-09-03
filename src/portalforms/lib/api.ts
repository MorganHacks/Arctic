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

/** The question types a form can ask. Spelled as the API spells them. */
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
  | "file";

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
 * A form behind its code.
 *
 * `fields` is absent when the form has closed. That is the API being careful
 * rather than terse: a closed form with its questions attached invites a page
 * that renders them behind a banner somebody scrolls straight past.
 */
export type PublicForm = {
  code: string;
  name: string;
  kind: string;
  open: boolean;
  closesAt: string | null;
  version?: number;
  fields?: Field[];
};

/**
 * Resolves the code in the URL. Null when there is no form to show.
 *
 * Null covers a code nobody issued, a code with a typo in it, and a form whose
 * only version is still a draft. The API answers all three the same way on
 * purpose — telling them apart would be a way to find out which
 * seven-character codes are real — and so does this page.
 */
export async function loadForm(code: string): Promise<PublicForm | null> {
  let response: Response;

  try {
    response = await fetch(
      `${apiOrigin}/api/forms/${encodeURIComponent(code)}`,
      {
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
