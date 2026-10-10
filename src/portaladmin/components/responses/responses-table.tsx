"use client";

import { ArrowDown01Icon, ArrowRight01Icon, Attachment01Icon } from "@hugeicons/core-free-icons";
import type { CSSProperties } from "react";
import { Icon } from "@/components/ui/icon";
import { Skeleton } from "@/components/ui/skeleton";
import type { FormField } from "@/lib/api";
import { AnswerCell } from "./answers";
import type { Column } from "./columns";
import { ResponsePerson } from "./response-person";
import { respondentFor, submittedAt } from "./respondent";
import styles from "./responses.module.css";
import type { ResponseItem } from "./types";

export function ResponsesTable({ fields, items, openId, onOpen, showResume, columns, loading = false }: {
  fields: FormField[];
  items: ResponseItem[];
  openId: string | null;
  onOpen: (id: string) => void;
  showResume: boolean;
  columns: Column[];
  loading?: boolean;
}) {
  // A resume column on a form that never asked for one is an empty column on
  // every row forever.
  const resumes = showResume && items.some((item) => item.resume !== null);

  /*
   * How wide each answer column is allowed to be.
   *
   * Ceilings, not measurements: the table lays out at `table-layout: fixed`,
   * so nothing here grows with what anybody wrote. An answer longer than its
   * column is clipped with an ellipsis, its whole text stays in the cell for
   * a screen reader and in the title for anyone who hovers it, and the panel
   * one click or Enter away holds it in full — which is what makes a ceiling
   * safe to have at all.
   *
   * What the number decides is how much screen the table asks for. The three
   * columns that are not answers — respondent, submitted, resume — come to
   * 560px between them, and a 1440px window with the sidebar open has 1080px
   * of content, so three answer columns have about 520px to share. The old
   * widths took 604 and put the table 155px past the edge of its own box,
   * which is how the resume column came to sit half hidden behind a sideways
   * scroll nobody had asked for. Enough columns to pass the box and it still
   * scrolls; the sticky first column is there for that.
   */
  const widths = columns.map((column) => {
    switch (column.field?.type) {
      case "number": return 76;
      case "phone": return 140;
      case "date": return 128;
      case "email": return 208;
      case "consent": return 156;
      default: return 176;
    }
  });

  /*
   * The two fixed columns that are not answers, named once each. The table's
   * minimum width is built from them as well as from the colgroup below, and
   * two copies of a number is a table that scrolls sideways for a reason no
   * single line explains.
   */
  const submittedWidth = 152;
  const resumeWidth = 144;

  return (
    <div className={styles.scroll} role="region" aria-label="Responses table" tabIndex={0}>
      <table className={styles.table} aria-label="Form responses" aria-busy={loading}
        style={{ "--answer-columns-width": `${submittedWidth + widths.reduce((total, width) => total + width, 0) + (resumes ? resumeWidth : 0)}px` } as CSSProperties}>
        <colgroup>
          <col className={styles.respondentColumn} />
          <col style={{ width: submittedWidth }} />
          {columns.map((column, index) => <col key={column.key} style={{ width: widths[index] }} />)}
          {resumes ? <col style={{ width: resumeWidth }} /> : null}
        </colgroup>
        <thead>
          <tr>
            <th className={styles.respondent} scope="col">Respondent</th>
            <th scope="col" aria-sort="descending"><span className={styles.submittedHeading}>Submitted<Icon icon={ArrowDown01Icon} size={14} /></span></th>
            {columns.map((column) => (
              <th key={column.key} scope="col"
                data-numeric={column.field?.type === "number" || undefined}
                className={column.kind === "retired" ? styles.retired : undefined}
                title={column.kind === "retired" ? "No longer on this form" : column.label}>
                {column.label}
              </th>
            ))}
            {resumes ? <th scope="col">Resume</th> : null}
          </tr>
        </thead>
        <tbody>
          {items.map((item) => {
            const submitted = submittedAt(item.submittedAt);
            return (
              <tr key={item.id} className={item.id === openId ? `${styles.row} ${styles.selected}` : styles.row}
                onClick={() => onOpen(item.id)}>
                <td className={styles.respondent}>
                  <button type="button" className={styles.open}
                    aria-label={`Open response from ${respondentFor(item, fields).name}, ${submitted.date} at ${submitted.time}`}
                    aria-haspopup="dialog"
                    onClick={(event) => { event.stopPropagation(); onOpen(item.id); }}>
                    <ResponsePerson item={item} fields={fields} />
                    <Icon icon={ArrowRight01Icon} size={16} className={styles.openArrow} />
                  </button>
                </td>
                <td className={styles.when}>
                  <time dateTime={item.submittedAt} title={`${submitted.date}, ${submitted.time}`}>
                    <span>{submitted.date}</span>
                    <span className={styles.submissionMeta}>{submitted.time}<span className={styles.version} title={`Form version ${item.formVersion}`}>v{item.formVersion}</span></span>
                  </time>
                </td>
                {columns.map((column) => (
                  <td key={column.key} className={styles.cell} data-numeric={column.field?.type === "number" || undefined}>
                    <AnswerCell value={item.answers[column.key]} field={column.field} />
                  </td>
                ))}
                {resumes ? (
                  <td className={styles.cell}>
                    {item.resume ? (
                      <span className={styles.fileCell} title={item.resume.filename}>
                        <Icon icon={Attachment01Icon} size={15} /><span>Resume</span>
                        <small>{item.resume.filename.match(/\.([a-z0-9]{1,5})$/i)?.[1].toUpperCase() || "FILE"}</small>
                      </span>
                    ) : <span className={styles.blank}>—</span>}
                  </td>
                ) : null}
              </tr>
            );
          })}
          {loading ? Array.from({ length: 3 }, (_, index) => <tr key={`loading-${index}`} aria-hidden="true">
            <td><Skeleton width="75%" height={14} /></td>
            <td><Skeleton width="60%" height={11} /></td>
            {columns.map(column => <td key={column.key}><Skeleton width="70%" height={11} /></td>)}
            {resumes ? <td><Skeleton width={70} height={24} /></td> : null}
          </tr>) : null}
        </tbody>
      </table>
    </div>
  );
}
