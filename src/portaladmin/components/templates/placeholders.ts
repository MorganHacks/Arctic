/**
 * The `{{` somebody is in the middle of typing, and what to do about it.
 *
 * No DOM and no React in this file on purpose. Working out whether the caret
 * is inside an unfinished placeholder, which names match what has been typed,
 * and what the text should read afterwards are three decisions that have
 * nothing to do with where a menu is drawn — and keeping them here is what
 * makes the awkward cases (`{{{`, an already-closed `{{name}}`, a `}}` sitting
 * to the right of the caret) answerable by reading one short function rather
 * than by typing into a browser.
 *
 * The character set is the one `placeholdersIn` reads in types.ts. If the two
 * ever disagree, the menu offers a name the list under the editor will not
 * recognise back.
 */

import type { Placeholder } from "./types";

/** What a placeholder name may be made of, matched against what is typed so far. */
const NAME = /^[\w.]*$/;

export type Trigger = {
  /** Index of the first `{` of the `{{` that opened this. */
  start: number;
  /** What has been typed between the `{{` and the caret. Often empty. */
  query: string;
};

/**
 * The unfinished placeholder the caret sits in, or null.
 *
 * Null is the common answer and is the reason this is safe to call on every
 * keystroke: an author writing prose is outside a `{{` almost all of the time,
 * and the moment what they have typed stops looking like a name — a space, a
 * brace, anything — this returns null and the menu goes away rather than
 * following them down the line.
 */
export function triggerAt(value: string, caret: number): Trigger | null {
  if (caret < 2) {
    return null;
  }

  // Searching backwards from `caret - 2` so the `{{` has to be fully behind
  // the caret. A negative index would make lastIndexOf search from 0 and match
  // a brace pair the caret has not reached yet.
  const start = value.lastIndexOf("{{", caret - 2);
  if (start < 0) {
    return null;
  }

  const query = value.slice(start + 2, caret);
  if (!NAME.test(query)) {
    return null;
  }

  return { start, query };
}

/**
 * The names worth offering for what has been typed.
 *
 * Names that start with the query come before names that merely contain it,
 * because somebody typing `fi` means `firstName` far more often than they mean
 * something with `fi` in the middle. Within each group the API's own order is
 * kept: it knows which names matter and this does not.
 *
 * Case is ignored while matching and never altered on the way out — the sender
 * matches placeholders exactly, so `FirstName` typed into the box must still
 * insert `firstName`.
 */
export function matching(
  available: Placeholder[],
  query: string,
): Placeholder[] {
  if (query === "") {
    return available;
  }

  const wanted = query.toLowerCase();
  const starts: Placeholder[] = [];
  const contains: Placeholder[] = [];

  for (const placeholder of available) {
    const name = placeholder.name.toLowerCase();

    if (name.startsWith(wanted)) {
      starts.push(placeholder);
    } else if (name.includes(wanted)) {
      contains.push(placeholder);
    }
  }

  return [...starts, ...contains];
}

/** The text after an insertion, and where the caret belongs in it. */
export type Insertion = { value: string; caret: number };

/**
 * Writes the chosen name in, closing braces and all.
 *
 * The braces to the right of the caret are consumed rather than added to. An
 * author who typed `{{}}` and then went back between them is the ordinary
 * case, and a menu that answered it with `{{firstName}}}}` would be worse than
 * one that offered nothing — they would have to notice and fix it every time.
 */
export function insert(
  value: string,
  trigger: Trigger,
  caret: number,
  name: string,
): Insertion {
  const after = value.slice(caret);
  const closing = after.startsWith("}}") ? 2 : after.startsWith("}") ? 1 : 0;

  const head = `${value.slice(0, trigger.start)}{{${name}}}`;

  return { value: head + after.slice(closing), caret: head.length };
}
