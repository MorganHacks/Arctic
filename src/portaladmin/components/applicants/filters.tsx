"use client";

import { useRef } from "react";
import { Calendar03Icon, Search01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { Select } from "@/components/ui/select";
import Form from "next/form";
import Link from "next/link";
import styles from "./applicants-list.module.css";
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
  const form = useRef<HTMLFormElement>(null);

  // Every status with rows on this event, plus any the reader has already
  // picked. The second half matters: a filter that matches nothing has to stay
  // on the bar, or there is no way to press it again to turn it off.
  const shown = STATUSES.filter(
    (status) => (counts[status] ?? 0) > 0 || chose.has(status),
  );

  return (
    <>
      <Form ref={form} key={`${chosen.id}:${q}:${statuses.join(",")}`} action="/applicants" className={styles.controls} role="search" aria-label="Find applicants">
        <div className={styles.eventPicker}>
          <Icon icon={Calendar03Icon} size={18} />
          {events.length > 1 ? (
            <Select aria-label="Event" name="event" defaultValue={chosen.id} onChange={() => form.current?.requestSubmit()}>
              {events.map((event) => (
                <option key={event.id} value={event.id}>
                  {event.name}
                </option>
              ))}
            </Select>
          ) : <><span>{chosen.name}</span><input type="hidden" name="event" value={chosen.id} /></>}
        </div>

        <div className={styles.searchActions}>
          <div className={styles.search}>
            <Icon icon={Search01Icon} size={18} />
            <input
              aria-label="Search applicants by name or email"
              name="q"
              type="search"
              enterKeyHint="search"
              placeholder="Search by name or email"
              defaultValue={q}
            />
          </div>

          {statuses.map((status) => (
            <input key={status} type="hidden" name="status" value={status} />
          ))}

          {q || statuses.length > 0 ? (
            <Link href={to(chosen.id, "", [])} className={styles.clear} scroll={false}>
              Clear filters
            </Link>
          ) : null}
        </div>
      </Form>

      {/*
        One strip of counts, doing two jobs at once.
        Read down it and you know where the event is; press one and the list
        below narrows to it. The mockup drew this twice — once above a row of
        saved views and once below — and a number that appears twice on one
        screen is a number a reader has to check against itself.
      */}
      <ul className={styles.tallies} aria-label="Filter applicants by status">
        <li>
          <Link
            href={to(chosen.id, q, [])}
            scroll={false}
            aria-current={statuses.length === 0 ? "true" : undefined}
            className={
              statuses.length === 0 ? `${styles.tally} ${styles.on}` : styles.tally
            }
          >
            <span className={styles.tallyLabel}>All applicants</span>
            <span className={styles.tallyCount}>
              {total(counts).toLocaleString("en-US")}
            </span>
          </Link>
        </li>

        {shown.map((status) => (
          <li key={status}>
            {/* Toggles rather than replaces. The useful filters are groups —
                everything undecided, accepted or confirmed — and one at a time
                would mean reading two lists and merging them by eye. */}
            <Link
              href={to(chosen.id, q, toggle(statuses, status))}
              scroll={false}
              aria-current={chose.has(status) ? "true" : undefined}
              className={[
                styles.tally,
                chose.has(status) ? styles.on : "",
              ]
                .filter(Boolean)
                .join(" ")}
            >
              <span className={styles.tallyLabel}>{label(status)}</span>
              <span className={styles.tallyCount}>
                {(counts[status] ?? 0).toLocaleString("en-US")}
              </span>
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
