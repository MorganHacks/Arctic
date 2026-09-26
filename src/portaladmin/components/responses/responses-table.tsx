"use client";

import { ArrowDown01Icon, ArrowRight01Icon, Attachment01Icon } from "@hugeicons/core-free-icons";
import type { CSSProperties } from "react";
import { Icon } from "@/components/ui/icon";
import type { FormField } from "@/lib/api";
import { AnswerCell } from "./answers";
import type { Column } from "./columns";
import { ResponsePerson } from "./response-person";
import { respondentFor, submittedAt } from "./respondent";
import styles from "./responses.module.css";
import type { ResponseItem } from "./types";

export function ResponsesTable({ fields, items, openId, onOpen, showResume, columns }: {
  fields: FormField[];
  items: ResponseItem[];
  openId: string | null;
  onOpen: (id: string) => void;
  showResume: boolean;
  columns: Column[];
}) {
  // A resume column on a form that never asked for one is an empty column on
  // every row forever.
  const resumes = showResume && items.some((item) => item.resume !== null);
  const widths = columns.map((column) => {
    switch (column.field?.type) {
      case "number": return 80;
      case "phone": return 156;
      case "date": return 140;
      case "email": return 256;
      case "consent": return 180;
      default: return 224;
    }
  });

  return (
    <div className={styles.scroll} role="region" aria-label="Responses table" tabIndex={0}>
      <table className={styles.table} aria-label="Form responses"
        style={{ "--answer-columns-width": `${184 + widths.reduce((total, width) => total + width, 0) + (resumes ? 144 : 0)}px` } as CSSProperties}>
        <colgroup>
          <col className={styles.respondentColumn} />
          <col style={{ width: 184 }} />
          {columns.map((column, index) => <col key={column.key} style={{ width: widths[index] }} />)}
          {resumes ? <col style={{ width: 144 }} /> : null}
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
        </tbody>
      </table>
    </div>
  );
}
