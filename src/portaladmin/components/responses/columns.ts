import type { FormField } from "@/lib/api";
import type { ResponseItem } from "./types";

/**
 * What the table has a column for.
 *
 * Two kinds, because a form outlives its own questions. `asked` is a question
 * the form has now, and gets the label somebody wrote. `retired` is a key that
 * shows up in the answers and no longer belongs to any question — a question
 * that was deleted after people had already answered it — and there is no
 * label to show, because the label was deleted with it.
 */
export type Column =
  | { kind: "asked"; key: string; label: string; field: FormField }
  | { kind: "retired"; key: string; label: string; field: null };

/**
 * The two field types that are not a column.
 *
 * A file question's answer is the resume, which has its own column and its own
 * permission — a second column holding the same filename would be furniture.
 *
 * A page break is not a question at all. Nobody answered it and nothing is
 * stored under its key, so a column for it would be empty in every row under a
 * heading nobody was ever asked.
 */
const NOT_A_COLUMN = new Set(["file", "section"]);

/**
 * The columns for a set of loaded responses.
 *
 * The form's own questions first, in the order they are asked, whether or not
 * anybody has answered them — a question added last week has no answers yet
 * and its column being absent would read as the question being absent.
 *
 * Then whatever else the answers actually contain. Derived from the rows on
 * screen rather than from any list of past versions, because the only thing
 * that makes a retired key worth a column is that a response in front of the
 * reader has one.
 *
 * Recomputed as more pages load, so a retired key that first appears on page
 * four gains its column then. That moves the resume column right while
 * somebody is reading, which is worth it: the alternative is silently not
 * showing an answer.
 */
export function columnsFor(
  fields: FormField[],
  items: ResponseItem[],
): Column[] {
  const asked: Column[] = [];
  const known = new Set<string>();

  for (const field of fields) {
    if (NOT_A_COLUMN.has(field.type)) {
      // Not counted as known either, which is what makes the awkward case
      // work: a question turned into a page break keeps its key, and the
      // answers already given to it are still filed under it. They lose their
      // labelled column and become retired ones, which is exactly what they
      // are — and the alternative is a set of answers that is on nobody's
      // screen. A file question's key is never in the answers at all, so this
      // costs it nothing.
      continue;
    }

    known.add(field.key);

    asked.push({
      kind: "asked",
      key: field.key,
      // A question can be saved before it is worded. The key is what it is
      // filed under and is readable on purpose, so it stands in.
      label: field.label.trim() === "" ? field.key : field.label,
      field,
    });
  }

  const retired = new Set<string>();
  for (const item of items) {
    for (const key of Object.keys(item.answers)) {
      if (!known.has(key)) {
        retired.add(key);
      }
    }
  }

  return [
    ...asked,
    ...[...retired].sort().map(
      (key): Column => ({ kind: "retired", key, label: key, field: null }),
    ),
  ];
}

/**
 * The questions as the detail panel walks them.
 *
 * Everything the form asks, in order, including the file question — the panel
 * is where a resume is actually downloaded — followed by whatever this one
 * response holds that the form no longer asks. Unlike the table this is per
 * response, because "what else did this person tell us" is a question about
 * one submission.
 *
 * Page breaks are dropped. The panel shows every question without an answer as
 * "Not answered", and a page break has no answer and never will — leaving one
 * in would put a heading on the screen under a complaint that nobody filled it
 * in.
 */
export function askedAndRetired(
  fields: FormField[],
  item: ResponseItem,
): { asked: FormField[]; retired: string[] } {
  const asked = fields.filter((field) => field.type !== "section");
  const known = new Set(asked.map((field) => field.key));

  return {
    asked,
    retired: Object.keys(item.answers)
      .filter((key) => !known.has(key))
      .sort(),
  };
}
