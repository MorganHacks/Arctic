import Link from "next/link";
import styles from "./applicants.module.css";
import { STATUSES, label } from "./status";
import type { EventSummary, Status } from "./types";

/**
 * Which applicants the list is showing, as a URL.
 *
 * Every filter here is a query parameter and none of it is React state. An
 * organizer who finds a problem in the waitlist sends that link to whoever has
 * to act on it, and a filter held in state is one that cannot be sent to
 * anybody. It also means the back button works, which on a screen people spend
 * an afternoon in is not a small thing.
 *
 * The search is a plain GET form for the same reason, and it carries the
 * status filter across as hidden fields — searching within a filter is the
 * common move, and dropping the filter on submit would silently widen what
 * somebody is looking at.
 *
 * The page marker is deliberately not carried across any of this. Changing the
 * filter and keeping the old cursor would land the reader in the middle of a
 * different list, at a position that means nothing.
 */
export function Filters({
  events,
  chosen,
  q,
  statuses,
  counts,
}: {
  events: EventSummary[];
  chosen: EventSummary;
  q: string;
  statuses: Status[];
  counts: Partial<Record<Status, number>>;
}) {
  const chose = new Set(statuses);

  // Every status with rows on this event, plus any the reader has already
  // picked. The second half matters: a filter that matches nothing has to stay
  // on the bar, or there is no way to press it again to turn it off.
  const shown = STATUSES.filter(
    (status) => (counts[status] ?? 0) > 0 || chose.has(status),
  );

  return (
    <>
      <form method="get" action="/applicants" className="filters">
        {events.length > 1 ? (
          <div>
            <label htmlFor="event">Event</label>
            <select id="event" name="event" defaultValue={chosen.id}>
              {events.map((event) => (
                <option key={event.id} value={event.id}>
                  {event.name}
                </option>
              ))}
            </select>
          </div>
        ) : (
          <input type="hidden" name="event" value={chosen.id} />
        )}

        <div className="grow">
          <label htmlFor="q">Search</label>
          <input
            id="q"
            name="q"
            type="search"
            placeholder="Name or email"
            defaultValue={q}
            style={{ width: "100%" }}
          />
        </div>

        {statuses.map((status) => (
          <input key={status} type="hidden" name="status" value={status} />
        ))}

        <button type="submit">Search</button>

        {q || statuses.length > 0 ? (
          <Link href={to(chosen.id, "", [])} className="button">
            Clear
          </Link>
        ) : null}
      </form>

      <ul className={styles.tallies}>
        <li>
          <Link
            href={to(chosen.id, q, [])}
            className={
              statuses.length === 0 ? `${styles.tally} ${styles.on}` : styles.tally
            }
          >
            All
            <span className={styles.count}>{total(counts)}</span>
          </Link>
        </li>

        {shown.map((status) => (
          <li key={status}>
            {/* Toggles rather than replaces. The useful filters are groups —
                everything undecided, accepted or confirmed — and one at a time
                would mean reading two lists and merging them by eye. */}
            <Link
              href={to(chosen.id, q, toggle(statuses, status))}
              className={
                chose.has(status) ? `${styles.tally} ${styles.on}` : styles.tally
              }
            >
              {label(status)}
              <span className={styles.count}>{counts[status] ?? 0}</span>
            </Link>
          </li>
        ))}
      </ul>
    </>
  );
}

function total(counts: Partial<Record<Status, number>>): number {
  let sum = 0;
  for (const status of STATUSES) {
    sum += counts[status] ?? 0;
  }

  return sum;
}

function toggle(statuses: Status[], status: Status): Status[] {
  return statuses.includes(status)
    ? statuses.filter((one) => one !== status)
    : [...statuses, status];
}

/** The list, filtered this way. */
function to(event: string, q: string, statuses: Status[]): string {
  const params = new URLSearchParams({ event });

  if (q) {
    params.set("q", q);
  }

  for (const status of statuses) {
    params.append("status", status);
  }

  return `/applicants?${params}`;
}
