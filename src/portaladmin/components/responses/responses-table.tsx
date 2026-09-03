"use client";

import type { FormField } from "@/lib/api";
import { AnswerCell, when } from "./answers";
import { columnsFor } from "./columns";
import styles from "./responses.module.css";
import type { ResponseItem } from "./types";

/**
 * Every loaded response, one to a row.
 *
 * A scanning surface. Somebody works down this looking for the row worth
 * opening, so everything is one line high, dates read in the order they sort,
 * and numbers line up under each other. Nothing is coloured except the row
 * that is open, which is the only thing on the screen that changes what a
 * click does next.
 */
export function ResponsesTable({
  fields,
  items,
  openId,
  onOpen,
  showResume,
}: {
  fields: FormField[];
  items: ResponseItem[];
  openId: string | null;
  onOpen: (id: string) => void;
  /** Whether this person may read resumes at all. */
  showResume: boolean;
}) {
  const columns = columnsFor(fields, items);

  // A resume column on a form that never asked for one is an empty column on
  // every row forever.
  const resumes = showResume && items.some((item) => item.resume !== null);

  return (
    <div className={styles.scroll}>
      <table className={styles.table}>
        <thead>
          <tr>
            <th className={styles.when} scope="col">
              Submitted
            </th>
            <th scope="col">Version</th>

            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                className={column.kind === "retired" ? styles.retired : undefined}
                // Said in a tooltip rather than in the header, because a
                // header wide enough to explain itself is a column somebody
                // has to scroll past on every row.
                title={column.kind === "retired" ? "No longer on this form" : undefined}
              >
                {column.label}
              </th>
            ))}

            {resumes ? <th scope="col">Resume</th> : null}
          </tr>
        </thead>

        <tbody>
          {items.map((item) => (
            <tr
              key={item.id}
              className={
                item.id === openId ? `${styles.row} ${styles.selected}` : styles.row
              }
              onClick={() => onOpen(item.id)}
            >
              <td className={styles.when}>
                {/* The date is the button. A row is not focusable and a table
                    full of rows that only a mouse can open is a table half the
                    organizers cannot use. */}
                <button
                  type="button"
                  className={styles.open}
                  onClick={(event) => {
                    event.stopPropagation();
                    onOpen(item.id);
                  }}
                >
                  {when(item.submittedAt)}
                </button>
              </td>

              {/* Which version of the form this was answered on. The reason a
                  row has gaps where its neighbours do not, so it belongs
                  beside them rather than buried in the panel. */}
              <td className={styles.version}>v{item.formVersion}</td>

              {columns.map((column) => (
                <td key={column.key} className={styles.cell}>
                  <AnswerCell
                    value={item.answers[column.key]}
                    field={column.field}
                  />
                </td>
              ))}

              {resumes ? (
                <td className={styles.cell}>
                  {item.resume ? (
                    <span title={item.resume.filename}>{item.resume.filename}</span>
                  ) : (
                    <span className={styles.blank}>—</span>
                  )}
                </td>
              ) : null}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
