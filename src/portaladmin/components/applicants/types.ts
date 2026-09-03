/**
 * What an applicant looks like on the way out of the API.
 *
 * Separate from the responses types next door, which describe a submission on
 * a form. The same row is behind both and they are answering different
 * questions: that one is "what did people answer", this one is "who is this
 * and what happens to them next".
 */

/**
 * Where an application is in its life.
 *
 * The stored spelling, which is what the API sends and what the column holds.
 * A union rather than `string` because every one of these has a label and a
 * colour on this screen, and a status nobody wrote a case for should be a type
 * error here rather than a blank cell in front of a reviewer.
 */
export type Status =
  | "incomplete"
  | "submitted"
  | "under_review"
  | "accepted"
  | "rejected"
  | "waitlisted"
  | "confirmed"
  | "declined"
  | "expired"
  | "checked_in"
  | "withdrawn";

/** One row on the list. Less than the record carries, on purpose. */
export type ApplicantRow = {
  id: string;
  email: string;
  firstName: string | null;
  lastName: string | null;
  school: string | null;
  status: Status;
  createdAt: string;
  submittedAt: string | null;
  decidedAt: string | null;

  /** Whether there is a file worth opening. Never where it is. */
  hasResume: boolean;
};

/**
 * A page of applicants, newest first.
 *
 * A cursor rather than a page number. Applications only arrive at the newest
 * end, so an offset taken before registration closed and used after it would
 * show one applicant twice and skip another — and the one it skipped never
 * gets a decision.
 */
export type ApplicantPage = {
  items: ApplicantRow[];
  nextCursor: string | null;
};

export type EventSummary = {
  id: string;
  slug: string;
  name: string;
  startsAt: string | null;
};

/** The list, the events to choose from, and how many are in each status. */
export type ApplicantsView = {
  events: EventSummary[];
  chosen: EventSummary | null;

  /**
   * Counts for the whole event, not for the filtered set.
   *
   * They are what the filters are chosen from — "how many are still
   * undecided" is the question that decides which filter to press — and counts
   * that moved with the filter would only ever confirm what the filter already
   * said. Statuses with no rows are absent rather than zero.
   */
  counts: Partial<Record<Status, number>>;

  items: ApplicantRow[];
  nextCursor: string | null;
};

/**
 * One answer, with the question it was given to.
 *
 * Joined on the server. Answers are stored under a question's key and never
 * its label, which is what lets a form be edited after somebody has answered
 * it — and the cost is that a page of answers is unreadable without the form.
 * The responses table ships the whole form definition and joins in the
 * browser because it draws a column per question; this screen shows one
 * person, and shipping every version of a twenty-question form to label twenty
 * answers would be a lot of payload for a join the server already did.
 *
 * `label` is null for an answer whose question the form no longer publishes.
 * The wording was deleted with the question and the key is all that is left of
 * it. It is still what somebody wrote.
 *
 * `value` is `unknown` rather than a union of the shapes a field type
 * produces. That union would describe today's form; this holds what somebody
 * actually answered, and the only honest type for it is one that has to be
 * narrowed before it is read.
 */
export type Answer = {
  key: string;
  label: string | null;
  value: unknown;
};

/**
 * One recorded step in an application's life.
 *
 * `actorId` is null where nobody was behind it — the applicant did it
 * themselves, the RSVP expiry job did it, or somebody fixed a row by hand.
 * That is the honest record rather than a gap, and putting a name against a
 * decision nobody made would be worse than admitting there is not one.
 */
export type Step = {
  from: Status | null;
  to: Status;
  actorId: string | null;
  reason: string | null;
  batchId: string | null;
  at: string;
};

/** One internal note. Never shown to the applicant. */
export type Note = {
  id: string;
  authorId: string;
  body: string;
  createdAt: string;
};

/**
 * The resume, where this reader may have one.
 *
 * The URL is signed and lives about five minutes, which is why it only ever
 * arrives with a single record and never with a list. `expiresAt` is handed
 * over so the screen knows when to ask for a fresh one rather than discovering
 * the problem as a broken download.
 */
export type Resume = {
  filename: string;
  sizeBytes: number | null;
  url: string;
  expiresAt: string;
};

/**
 * One applicant in full.
 *
 * `answers`, `notes` and `resume` are null where this person may not read
 * them, and empty where there is nothing to read. The screen says something
 * different for each, because "you cannot see this" and "there is nothing
 * here" are different sentences and only one of them is true.
 */
export type Applicant = {
  id: string;
  eventId: string;
  email: string;
  firstName: string | null;
  lastName: string | null;
  school: string | null;

  status: Status;

  /**
   * Where this application can still go, from the API's own lifecycle table.
   *
   * Not a copy of that table in TypeScript. A console that offered a move the
   * API refuses would be a button whose only outcome is an error message.
   */
  allowedNext: Status[];

  formVersion: number;
  createdAt: string;
  submittedAt: string | null;
  decidedAt: string | null;
  rsvpDeadline: string | null;
  confirmedAt: string | null;
  declinedAt: string | null;
  checkedInAt: string | null;

  hasResume: boolean;
  resume: Resume | null;

  history: Step[];
  answers: Answer[] | null;
  notes: Note[] | null;
};

/** What the "load more" action answers with. */
export type PageResult =
  | { ok: true; page: ApplicantPage }
  | { ok: false; error: string };
