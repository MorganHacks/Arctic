import type { Field, PublicForm } from "./api";

/**
 * A form with sections in it, made up here, for looking at the multi-step page
 * without an API that can serve one yet.
 *
 * ## This is scaffolding. Delete it.
 *
 * It exists because the `section` field type is being added on the other side
 * of the wire at the same time as this page learned to read it, and a page
 * nobody can look at is a page nobody can review. The moment the API serves a
 * form with sections in it, this file and the two lines in `lib/api.ts` that
 * call it should go. Nothing else imports it and nothing else knows it is here.
 *
 * ## It cannot reach anybody
 *
 * Two locks, and both have to be open:
 *
 *   - `NODE_ENV` must not be `production`, so a build that ships cannot serve
 *     it however the environment is configured;
 *   - `FORMS_PREVIEW` must be `1`, so a developer running `next dev` normally
 *     does not find a form behind a code they did not create.
 *
 * The wording below is placeholder and has not been through the product owner.
 * It says nothing about the event — no dates, no venue, no promises — because
 * the fastest way for invented copy to ship is for it to be sitting in a file
 * that looks finished.
 */
export function previewForm(code: string): PublicForm | null {
  if (process.env.NODE_ENV === "production" || process.env.FORMS_PREVIEW !== "1") {
    return null;
  }

  if (code === STEPPED) {
    return { ...shell, code, name: "Preview form (sections)", fields: stepped };
  }

  if (code === FLAT) {
    return { ...shell, code, name: "Preview form (no sections)", fields: flat };
  }

  return null;
}

/** A form cut into steps. */
const STEPPED = "preview";

/** The same questions with no sections at all — the single page, unchanged. */
const FLAT = "previewflat";

const shell = {
  kind: "application",
  open: true,
  closesAt: null,
  version: 1,
  requiresSignIn: false,
  access: "open" as const,
};

function field(part: Partial<Field> & Pick<Field, "key" | "type" | "label">): Field {
  return {
    help: null,
    required: false,
    options: [],
    minLength: null,
    maxLength: null,
    min: null,
    max: null,
    ...part,
  };
}

/*
 * Deliberately awkward in the places that matter: a question before the first
 * section, a section whose only content is its own description, a required
 * question on a later step, and a step with two questions on it. Those are the
 * four shapes worth being able to look at.
 */
const stepped: Field[] = [
  field({ key: "email", type: "email", label: "Email address", required: true }),

  field({
    key: "about",
    type: "section",
    label: "About you",
    help: "Placeholder description. Not approved copy.",
  }),
  field({ key: "name", type: "shortText", label: "Full name", required: true }),
  field({
    key: "school",
    type: "shortText",
    label: "School",
    help: "Placeholder help text.",
  }),
  field({
    key: "year",
    type: "select",
    label: "Year of study",
    options: [
      { value: "1", label: "First" },
      { value: "2", label: "Second" },
      { value: "3", label: "Third" },
      { value: "4", label: "Fourth or later" },
    ],
  }),

  field({
    key: "interlude",
    type: "section",
    label: "Before the next part",
    help: "A section with nothing under it. Placeholder copy.",
  }),

  field({ key: "why", type: "section", label: "Why you want to come" }),
  field({
    key: "pitch",
    type: "paragraph",
    label: "Tell us what you would like to build",
    required: true,
    maxLength: 600,
  }),
  field({
    key: "level",
    type: "radio",
    label: "How much have you built before?",
    required: true,
    options: [
      { value: "none", label: "Nothing yet" },
      { value: "some", label: "A few things" },
      { value: "lots", label: "Plenty" },
    ],
  }),

  field({ key: "last", type: "section", label: "Last bit" }),
  field({ key: "resume", type: "file", label: "Resume" }),
  field({
    key: "agree",
    type: "consent",
    label: "Placeholder agreement text. Not approved copy.",
    required: true,
  }),
];

const flat: Field[] = stepped.filter((one) => one.type !== "section");
