/**
 * What a submission looks like on the way out of the API.
 *
 * Answers are keyed by each question's `key`, never by its label or its
 * position. That is the whole reason a form can be edited after somebody has
 * answered it: rewording question three or moving it to the bottom leaves the
 * key alone, so every answer already given still lines up with the question it
 * was given to.
 *
 * The consequence, which every screen here has to survive: a response from
 * March carries the keys the form had in March. It will be missing keys the
 * form has gained since, and it may carry keys the form has since dropped.
 * Both are ordinary, neither is an error, and a viewer that treats either as
 * one is a viewer that breaks a week after the form is edited.
 */

/**
 * One answer, as JSON.
 *
 * `unknown` rather than a union of the shapes the field types produce. The
 * union would describe what today's form asks; this holds what somebody
 * actually answered, including under a key no question has any more, and the
 * only honest type for that is one that must be narrowed before it is read.
 */
export type AnswerMap = Record<string, unknown>;

/** The resume attached to a submission, where there is one. */
export type ResumeRef = {
  filename: string;
  sizeBytes: number;

  /**
   * Only on the single-response endpoint, and only for about five minutes.
   *
   * Absent from the list on purpose. A signed link minted while a page of
   * fifty rows loads has expired by the time somebody scrolls to the row that
   * needed it, and minting fifty of them leaves fifty live links behind to
   * read one file.
   */
  url?: string | null;
};

/** One submission. */
export type ResponseItem = {
  id: string;
  submittedAt: string;

  /** Which version of the form was on screen when this was answered. */
  formVersion: number;

  answers: AnswerMap;
  resume: ResumeRef | null;
};

/**
 * A page of submissions, newest first.
 *
 * A cursor rather than a page number. Submissions only arrive at the newest
 * end, so an offset taken before registration closed and used after it would
 * show one response twice and skip another.
 */
export type ResponsePage = {
  items: ResponseItem[];
  nextCursor: string | null;
};

/** What the "load more" action answers with. */
export type PageResult =
  | { ok: true; page: ResponsePage }
  | { ok: false; error: string };

/** What opening one response answers with. */
export type ItemResult =
  | { ok: true; item: ResponseItem }
  | { ok: false; error: string };
