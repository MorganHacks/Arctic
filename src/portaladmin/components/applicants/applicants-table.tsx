"use client";

import Link from "next/link";
import { useState } from "react";
import { Attachment01Icon } from "@hugeicons/core-free-icons";
import { Avatar } from "@/components/ui/avatar";
import { Icon } from "@/components/ui/icon";
import styles from "./applicants-list.module.css";
import { StatusPill } from "./status";
import type { ApplicantRow, PageResult } from "./types";

/** How many placeholder rows stand in for a page on its way. */
const WAITING = 3;

/**
 * Every loaded applicant, one to a row.
 *
 * A scanning surface. Somebody works down this looking for the row worth
 * opening, so everything is one line high, dates read in the order they sort,
 * and numbers line up under each other. The only colour is the status, which
 * is the one column that changes what a reader does next.
 *
 * Holds the loaded rows and nothing else about them. The first page arrives
 * rendered from the server; every page after it is appended here, so loading
 * page four does not re-fetch pages one to three — which matters more than it
 * sounds, because re-fetching from the start after registration closes would
 * mean five hundred rows crossing the wire to add fifty.
 */
export function ApplicantsTable({
  initialItems,
  initialCursor,
  loadMore,
  total,
}: {
  initialItems: ApplicantRow[];
  initialCursor: string | null;
  total: number | null;

  /** Bound to the current filter on the server. Returns the next page. */
  loadMore: (cursor: string) => Promise<PageResult>;
}) {
  const [items, setItems] = useState(initialItems);
  const [cursor, setCursor] = useState(initialCursor);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState<string | null>(null);

  async function more() {
    if (cursor === null || loading) {
      return;
    }

    setLoading(true);
    setFailed(null);

    let result: PageResult;
    try {
      result = await loadMore(cursor);
    } catch {
      setFailed("Could not load more applicants. Please try again.");
      setLoading(false);
      return;
    }

    if (!result.ok) {
      // The cursor is kept. A failed page is a page to try again, not the end
      // of the list, and dropping it would leave no way back to the rest.
      setFailed(result.error);
      setLoading(false);
      return;
    }

    setItems((current) => {
      // The same applicant arriving twice would render with a duplicate key
      // and be counted twice. Cheap to rule out, and the alternative is a bug
      // that only appears when somebody applies while a page is being turned.
      const seen = new Set(current.map((item) => item.id));
      return [...current, ...result.page.items.filter((item) => !seen.has(item.id))];
    });

    setCursor(result.page.nextCursor);
    setLoading(false);
  }

  return (
    <div className={styles.tableFrame}>
      <div className={styles.scroll} role="region" aria-label="Applicants table" tabIndex={0}>
        <table className={styles.table} aria-label="Applicants">
          <colgroup>
            <col className={styles.personColumn} />
            <col className={styles.schoolColumn} />
            <col className={styles.statusColumn} />
            <col className={styles.dateColumn} />
            <col className={styles.resumeColumn} />
          </colgroup>
          <thead>
            <tr>
              <th className={styles.who} scope="col">
                Applicant
              </th>
              <th scope="col">School</th>
              <th scope="col">Status</th>
              <th scope="col">Submitted</th>
              <th scope="col">Resume</th>
            </tr>
          </thead>

          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td className={styles.who}>
                  {/* A link, not a click handler on the row. Opening an
                      applicant is what this table is for, and a row only a
                      mouse can open is a table half the organizers cannot
                      use. */}
                  <Link href={`/applicants/${item.id}`} className={styles.open} aria-label={`View application from ${name(item)}`}>
                    <Avatar name={name(item)} email={item.email} appearance="soft" className={styles.avatar} />
                    <span className={styles.personText}>
                      <span className={styles.personName} title={name(item)}>{name(item)}</span>
                      <span className={styles.email} title={item.email}>{item.email}</span>
                    </span>
                  </Link>
                </td>

                <td className={styles.school}>
                  {item.school ?? <span className={styles.blank}>—</span>}
                </td>

                <td>
                  <StatusPill status={item.status} className={styles.status} />
                </td>

                <td className={styles.stamp}>
                  <SubmissionDate value={item.submittedAt} />
                </td>

                <td>
                  {item.hasResume ? <span className={styles.attachment}><Icon icon={Attachment01Icon} size={14} />Attached</span> : <span className={styles.blank}>—</span>}
                </td>
              </tr>
            ))}

            {/*
              Rows that have been asked for and have not arrived.
              Rows rather than a spinner, and the height of the real ones, so
              the table does not jump under the reader's cursor when the page
              lands. They are hidden from the accessibility tree: a screen
              reader announcing three empty rows would be describing furniture,
              and the button already says it is loading.
            */}
            {loading
              ? Array.from({ length: WAITING }, (_, index) => (
                  <tr key={`waiting-${index}`} className={styles.pending} aria-hidden>
                    <td className={styles.who}>
                      <span />
                    </td>
                    <td>
                      <span />
                    </td>
                    <td>
                      <span />
                    </td>
                    <td>
                      <span />
                    </td>
                    <td>
                      <span />
                    </td>
                  </tr>
                ))
              : null}
          </tbody>
        </table>
      </div>

      <div className={styles.foot}>
        <div className={styles.loaded}>
          <span className={styles.note} role="status">Showing <strong>{items.length.toLocaleString("en-US")}</strong>{total !== null ? ` of ${Math.max(total, items.length).toLocaleString("en-US")}` : ""} applicants</span>
          {failed ? <span className={styles.failed} role="alert">{failed}</span> : null}
        </div>

        {cursor !== null ? (
          <button type="button" className={styles.more} onClick={more} disabled={loading}>
            {loading ? "Loading…" : "Load more"}
          </button>
        ) : <span className={styles.note}>All results shown</span>}
      </div>
    </div>
  );
}

const dateFormat = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" });
const timeFormat = new Intl.DateTimeFormat("en-US", { hour: "numeric", minute: "2-digit", timeZone: "UTC", timeZoneName: "short" });

function SubmissionDate({ value }: { value: string | null }) {
  const date = value ? new Date(value) : null;
  if (!date || Number.isNaN(date.getTime())) return <span className={styles.blank}>Not submitted</span>;
  return <time dateTime={value!}><span>{dateFormat.format(date)}</span><span>{timeFormat.format(date)}</span></time>;
}

/**
 * What to call somebody in a list.
 *
 * Both names are nullable, because the row exists from the moment somebody
 * starts the form and the completeness constraint only applies once they
 * submit. A half-filled draft has an address and often nothing else, and the
 * address is the only thing left to identify them by.
 */
function name(item: ApplicantRow): string {
  const both = [item.firstName, item.lastName].filter(Boolean).join(" ");
  return both === "" ? item.email : both;
}
