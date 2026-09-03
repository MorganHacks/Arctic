import type { Field } from "@/lib/api";

/**
 * One screen of the form.
 *
 * A step is a heading and the questions under it. The heading is optional
 * because the first step usually has none: anything the form asks before its
 * first section is step one, and inventing a title for it would be putting
 * words in the form author's mouth.
 */
export type Step = {
  /** The section that opens this step, or null when nothing opens it. */
  section: Field | null;

  /** The questions asked here. Never contains a section. */
  fields: Field[];
};

export type Plan = {
  steps: Step[];

  /**
   * Every question's number, counted across the whole form.
   *
   * Counted across the form rather than restarting each step, because the
   * number is how somebody refers to a question out loud — "I'm stuck on nine"
   * has to mean one question, not one per step. Sections do not take a number:
   * they are not questions and nobody answers them.
   */
  ordinals: Record<string, number>;

  /** How many questions there are. Sections are not questions. */
  questions: number;
};

/**
 * Cuts a form's fields into steps.
 *
 * The rule is the whole of it: fields before the first section are step one,
 * and a section begins each step after that. A form with no sections comes back
 * as a single step, which is the same page it has always been — most forms have
 * none, and none of them should notice this file exists.
 *
 * A section with no questions under it still gets a step. That is a heading and
 * a description with nothing to fill in, which is a legitimate thing for a form
 * to want — an introduction, or a page of instructions before the questions
 * start — and quietly dropping it would delete something somebody wrote.
 */
export function plan(fields: Field[]): Plan {
  const steps: Step[] = [];
  const ordinals: Record<string, number> = {};
  let questions = 0;

  let current: Step = { section: null, fields: [] };

  for (const field of fields) {
    if (field.type === "section") {
      // A form that opens with a section does not get an empty step in front
      // of it. There is nothing before the first section to put there.
      if (current.section !== null || current.fields.length > 0) {
        steps.push(current);
      }

      current = { section: field, fields: [] };
      continue;
    }

    questions += 1;
    ordinals[field.key] = questions;
    current.fields.push(field);
  }

  if (current.section !== null || current.fields.length > 0) {
    steps.push(current);
  }

  // There is always at least one step, so nothing downstream has to hold an
  // opinion about an empty array. A form with no fields at all never reaches
  // here — the page shows it as nothing to fill in — but this is not the file
  // that should care.
  return {
    steps: steps.length > 0 ? steps : [current],
    ordinals,
    questions,
  };
}
