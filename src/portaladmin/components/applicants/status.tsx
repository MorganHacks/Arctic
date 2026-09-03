import styles from "./applicants.module.css";
import type { Status } from "./types";

/**
 * How a status is written and how it is coloured.
 *
 * One table, used by the list, the record, the filter bar and the history, so
 * a status cannot be spelled one way in a table and another in a pill three
 * inches away.
 *
 * The families are by what the reader does about it rather than by which
 * status it is. See the note in the stylesheet: most of the queue is undecided
 * for most of the cycle, and colouring that would leave nothing standing out
 * on the day decisions go out.
 */
const STATUS: Record<Status, { label: string; family: string }> = {
  incomplete: { label: "Incomplete", family: styles.undecided },
  submitted: { label: "Submitted", family: styles.undecided },
  under_review: { label: "Under review", family: styles.undecided },

  // Both are somebody who needs a second decision that nobody has made.
  waitlisted: { label: "Waitlisted", family: styles.waiting },
  expired: { label: "Expired", family: styles.waiting },

  accepted: { label: "Accepted", family: styles.in },
  confirmed: { label: "Confirmed", family: styles.in },
  checked_in: { label: "Checked in", family: styles.in },

  rejected: { label: "Rejected", family: styles.out },
  declined: { label: "Declined", family: styles.out },
  withdrawn: { label: "Withdrawn", family: styles.out },
};

/**
 * The order the filter bar and the status menu use.
 *
 * The lifecycle's own order, not alphabetical. Somebody looking for "under
 * review" looks for it after "submitted", because that is where it happens.
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

export function StatusPill({ status }: { status: Status }) {
  const known = STATUS[status];

  return (
    <span className={`${styles.status} ${known?.family ?? styles.undecided}`}>
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
