"use client";

import Link from "next/link";
import { useState } from "react";
import styles from "./applicants.module.css";
import { StatusPill, stamp } from "./status";
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
}: {
  initialItems: ApplicantRow[];
  initialCursor: string | null;

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

    const result = await loadMore(cursor);

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
    <>
      <div className={styles.scroll}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th className={styles.who} scope="col">
                Name
              </th>
              <th scope="col">Email</th>
              <th scope="col">School</th>
              <th scope="col">Status</th>
              {/* Said in the heading rather than converted per row. An
                  organizer comparing two applications needs them on one clock
                  more than they need their own. */}
              <th scope="col">Submitted (UTC)</th>
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
                  <Link href={`/applicants/${item.id}`} className={styles.open}>
                    {name(item)}
                  </Link>
                </td>

                <td className={styles.email} title={item.email}>
                  {item.email}
                </td>

                <td className={styles.school} title={item.school ?? undefined}>
                  {item.school ?? <span className={styles.blank}>—</span>}
                </td>

                <td>
                  <StatusPill status={item.status} />
                </td>

                <td className={styles.stamp}>
                  {stamp(item.submittedAt) ?? (
                    <span className={styles.blank}>not submitted</span>
                  )}
                </td>

                <td>
                  {item.hasResume ? "Yes" : <span className={styles.blank}>—</span>}
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
          <span className={styles.note}>{items.length} loaded</span>
          {failed ? <span className={styles.failed}>{failed}</span> : null}
        </div>

        {cursor !== null ? (
          <button type="button" onClick={more} disabled={loading}>
            {loading ? "Loading…" : "Load more"}
          </button>
        ) : null}
      </div>
    </>
  );
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
