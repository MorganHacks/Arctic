import styles from "./applicants.module.css";
import type { Status } from "./types";

/**
 * What a reader does about a status.
 *
 * Four families, and the split is by the reader's next move rather than by
 * which status it is. See the note in the stylesheet: most of the queue is
 * undecided for most of the cycle, and colouring that would leave nothing
 * standing out on the day decisions go out.
 *
 * Named rather than a class, because three surfaces render the same family
 * three different ways — a filled pill on a row, a bare figure on the count
 * strip, a mark on the history rail — and only the family is shared between
 * them. A class here would have forced the count strip either to wear a pill's
 * background or to invent a grouping of its own.
 */
export type Family = "undecided" | "waiting" | "in" | "out";

/**
 * How a status is written and which family it belongs to.
 *
 * One table, used by the list, the record, the filter bar and the history, so
 * a status cannot be spelled one way in a table and another in a pill three
 * inches away.
 */
const STATUS: Record<Status, { label: string; family: Family }> = {
  incomplete: { label: "Incomplete", family: "undecided" },
  submitted: { label: "Submitted", family: "undecided" },
  under_review: { label: "Under review", family: "undecided" },

  // Both are somebody who needs a second decision that nobody has made.
  waitlisted: { label: "Waitlisted", family: "waiting" },
  expired: { label: "Expired", family: "waiting" },

  accepted: { label: "Accepted", family: "in" },
  confirmed: { label: "Confirmed", family: "in" },
  checked_in: { label: "Checked in", family: "in" },

  rejected: { label: "Rejected", family: "out" },
  declined: { label: "Declined", family: "out" },
  withdrawn: { label: "Withdrawn", family: "out" },
};

/** The pill's fill, by family. */
const PILL: Record<Family, string> = {
  undecided: styles.undecided,
  waiting: styles.waiting,
  in: styles.in,
  out: styles.out,
};

/** The count strip's figure, by family. Colour on the number, never a fill. */
const FIGURE: Record<Family, string> = {
  undecided: styles.figureUndecided,
  waiting: styles.figureWaiting,
  in: styles.figureIn,
  out: styles.figureOut,
};

/** The mark on the history rail, by family. */
const MARK: Record<Family, string> = {
  undecided: styles.markUndecided,
  waiting: styles.markWaiting,
  in: styles.markIn,
  out: styles.markOut,
};

/**
 * The order the filter bar and the status menu use.
 *
 * The lifecycle's own order, not alphabetical and not grouped by colour.
 * Somebody looking for "under review" looks for it after "submitted", because
 * that is where it happens — and the three stages this runs through, a
 * decision, an RSVP and the day itself, are the order the season goes in.
 */
export const STATUSES: Status[] = [
  "incomplete",
  "submitted",
  "under_review",
  "accepted",
  "waitlisted",
  "rejected",
  "confirmed",
  "declined",
  "expired",
  "checked_in",
  "withdrawn",
];

export function label(status: Status): string {
  return STATUS[status]?.label ?? status;
}

export function family(status: Status): Family {
  return STATUS[status]?.family ?? "undecided";
}

/** The class for a count of this status on the strip. */
export function figureClass(status: Status): string {
  return FIGURE[family(status)];
}

/** The class for a mark against this status on the history rail. */
export function markClass(status: Status): string {
  return MARK[family(status)];
}

export function StatusPill({ status }: { status: Status }) {
  const known = STATUS[status];

  return (
    <span className={`${styles.status} ${PILL[known?.family ?? "undecided"]}`}>
      {known?.label ?? status}
    </span>
  );
}

/**
 * A timestamp, as something to compare rather than to read.
 *
 * The ISO string sliced, not formatted. Two reasons, and both matter more here
 * than a friendly date would.
 *
 * It is comparable. Largest unit first and fixed width means a column of these
 * sorts by eye, which is the whole job of a date in a list somebody is
 * scanning for the oldest thing nobody has looked at.
 *
 * And it is the same on both sides of the wire. Anything that reads the
 * machine's locale or timezone renders one string on the server and a
 * different one in the browser, which React reports as a hydration error and a
 * reader experiences as the page flickering. This is UTC, said so in the
 * heading rather than converted, because an organizer comparing two
 * applications needs them on one clock more than they need their own.
 */
export function stamp(iso: string | null): string | null {
  if (typeof iso !== "string" || iso.length < 16) {
    return null;
  }

  return `${iso.slice(0, 10)} ${iso.slice(11, 16)}`;
}
