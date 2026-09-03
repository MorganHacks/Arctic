import type { Field, FieldType } from "@/lib/api";
import type { Resume } from "./resume";

/** What one question's answer looks like while it is being filled in. */
export type Answer = string | string[] | boolean | Resume;

export type Answers = Record<string, Answer | undefined>;

/** A message per question key. Every problem the form knows about, at once. */
export type Problems = Record<string, string>;

/**
 * The types whose answer is prose, and therefore has a length worth checking.
 *
 * A date is a string too, and so is the value behind a radio button. Running a
 * length rule over those was the old behaviour and it could only ever produce a
 * message about a limit the API does not apply to them.
 */
const PROSE: ReadonlySet<FieldType> = new Set<FieldType>([
  "shortText",
  "paragraph",
  "email",
  "phone",
]);

/**
 * The cap the API applies when a question sets none.
 *
 * Mirrored rather than left to the server so that a paragraph somebody has
 * spent ten minutes on is not refused after the round trip. The numbers are
 * SubmissionValidation's; if they move there they have to move here, and the
 * cost of them drifting is one wasted submission rather than a wrong answer.
 */
function ceiling(field: Field): number {
  return field.maxLength ?? (field.type === "paragraph" ? 5000 : 500);
}

/**
 * The cap worth putting on the screen, or none.
 *
 * Only the one the form's author chose. Everything the API accepts has a
 * ceiling, but a counter under an email box counting up to five hundred is
 * noise about a limit nobody is going to reach — and a page that counts
 * everything trains people to read none of it.
 *
 * The unstated ceiling is still enforced. Somebody who does reach it is told
 * once, against the question, rather than watched all the way there.
 */
export function shownCap(field: Field): number | null {
  return PROSE.has(field.type) && field.maxLength != null ? field.maxLength : null;
}

/** Whether there is an answer here at all. Blank is absent, not empty. */
export function answered(field: Field, answer: Answer | undefined): boolean {
  if (answer === undefined || answer === null) {
    return false;
  }

  if (field.type === "consent") {
    return answer === true;
  }

  if (Array.isArray(answer)) {
    return answer.length > 0;
  }

  // A file is only an answer once its bytes are somewhere. A picked file whose
  // upload failed is not one — reading it as an answer is how a required
  // resume question passes with nothing behind it.
  if (typeof answer === "object") {
    return answer.upload.length > 0;
  }

  return String(answer).trim().length > 0;
}

/**
 * The same rules the API applies, checked early.
 *
 * Kept short deliberately. Anything subtle enough that the two copies could
 * drift belongs on the server alone — a message that appears here and not there
 * is confusing, and one that appears there and not here is merely a round trip.
 *
 * The one rule this side must never be stricter about is what an address is
 * allowed to look like. The API parses rather than pattern-matches precisely
 * because every regex anybody writes rejects somebody's real address, and for
 * the application form that address is the only way to reach them.
 */
export function check(field: Field, answer: Answer | undefined): string | null {
  /*
   * A section is a heading, not a question, so there is nothing here to be
   * wrong. The guard is at the top of the rule rather than at each caller
   * because a section that arrived marked `required` would otherwise fail a
   * check nobody can pass — there is no control to type into — and the form
   * would refuse to submit with no way past it.
   */
  if (field.type === "section") {
    return null;
  }

  if (!answered(field, answer)) {
    if (!field.required) {
      return null;
    }

    return field.type === "consent"
      ? "You have to agree to this to continue."
      : "This one is needed.";
  }

  if (typeof answer !== "string") {
    return null;
  }

  const value = answer.trim();

  if (field.type === "email" && !/^[^\s@]+@[^\s@]+$/.test(value)) {
    return "That does not look like an email address.";
  }

  if (field.type === "date" && !/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return "This has to be a date.";
  }

  if (field.type === "number") {
    const number = Number(value);

    if (!Number.isFinite(number)) {
      return "This has to be a number.";
    }

    if (field.min != null && number < field.min) {
      return `This cannot be below ${field.min}.`;
    }

    if (field.max != null && number > field.max) {
      return `This cannot be above ${field.max}.`;
    }
  }

  if (PROSE.has(field.type)) {
    if (field.minLength != null && value.length < field.minLength) {
      return `Needs at least ${field.minLength} characters.`;
    }

    const most = ceiling(field);
    if (value.length > most) {
      return `Has to be under ${most} characters.`;
    }
  }

  return null;
}

/**
 * What actually gets posted.
 *
 * Only the questions the form asked, and only the ones with something in them.
 * The API ignores anything else anyway — it validates against the version it
 * loaded, not against this list — so this is about sending a clean body rather
 * than about safety.
 */
export function payload(
  fields: Field[],
  answers: Answers,
): Record<string, unknown> {
  const body: Record<string, unknown> = {};

  for (const field of fields) {
    // A section is never answered, so it is never sent. Nothing ever writes
    // one into `answers`, but saying so here is what makes that a rule rather
    // than a happy consequence of no control rendering for it.
    if (field.type === "section") {
      continue;
    }

    const answer = answers[field.key];
    if (!answered(field, answer)) {
      continue;
    }

    if (typeof answer === "string") {
      body[field.key] = answer.trim();
    } else if (typeof answer === "object" && !Array.isArray(answer)) {
      // The upload id and nothing else. The name and the size are held here to
      // draw the row that says what is attached; the API took both from the
      // file while it had it, and sending our copies back would be us
      // describing a file it is already looking at.
      body[field.key] = { upload: answer.upload };
    } else {
      body[field.key] = answer;
    }
  }

  return body;
}

/**
 * A question's wording, trimmed to fit in a list of problems.
 *
 * The same length the API trims to, and for the same reason: MLH's
 * data-sharing agreement is sixty words, and quoted whole it buries the
 * complaint it is attached to.
 */
export function shorten(label: string): string {
  return label.length <= 48 ? label : `${label.slice(0, 45).trimEnd()}…`;
}
