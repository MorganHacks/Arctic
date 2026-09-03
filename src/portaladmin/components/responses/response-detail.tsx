"use client";

import { useEffect, useRef } from "react";
import type { FormField } from "@/lib/api";
import { AnswerBlock, fileSize, when } from "./answers";
import { askedAndRetired } from "./columns";
import styles from "./responses.module.css";
import type { ResponseItem } from "./types";

/**
 * One response, whole.
 *
 * Every question the form asks, in the order it asks them, whether or not this
 * person answered — a reviewer comparing two applicants needs the gaps to line
 * up, and a panel that silently omits the unanswered ones makes two
 * submissions look like they were asked different things.
 *
 * A panel rather than its own page. The table above it holds however many
 * pages somebody has loaded, and navigating away to read one response and
 * coming back to the first fifty rows is the kind of thing that makes people
 * stop using a screen.
 */
export function ResponseDetail({
  fields,
  item,
  loading,
  error,
  onClose,
}: {
  fields: FormField[];
  item: ResponseItem | null;
  loading: boolean;
  error: string | null;
  onClose: () => void;
}) {
  const panel = useRef<HTMLDivElement>(null);

  // Escape closes it, and opening moves focus into it. Without the second one
  // a keyboard reader who pressed the date button is still back in the table,
  // tabbing through rows behind a panel they cannot see.
  useEffect(() => {
    panel.current?.focus();

    const escape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
      }
    };

    document.addEventListener("keydown", escape);
    return () => document.removeEventListener("keydown", escape);
  }, [onClose]);

  const parts = item ? askedAndRetired(fields, item) : null;

  // A resume with no file question left to hang it under. The question was
  // deleted after somebody uploaded; the file is still theirs and still there.
  const orphanResume =
    item?.resume != null && !fields.some((field) => field.type === "file");

  return (
    <>
      <div className={styles.backdrop} onClick={onClose} />

      <div
        className={styles.panel}
        role="dialog"
        aria-modal="true"
        aria-label="Response"
        tabIndex={-1}
        ref={panel}
      >
        <div className={styles.panelHead}>
          <div>
            <h2>Response</h2>
            {item ? (
              <p className={styles.stamp}>
                {when(item.submittedAt)} · v{item.formVersion}
              </p>
            ) : null}
          </div>
          <button type="button" onClick={onClose}>
            Close
          </button>
        </div>

        {loading ? <p className={styles.unanswered}>Loading…</p> : null}
        {error ? <p className={styles.failed}>{error}</p> : null}

        {item && parts ? (
          <>
            {parts.asked.map((field) => (
              <div className={styles.qa} key={field.key}>
                <p className={styles.question}>
                  {field.label.trim() === "" ? (
                    <span className={styles.questionKey}>{field.key}</span>
                  ) : (
                    field.label
                  )}
                </p>

                {field.type === "file" ? (
                  <Resume item={item} />
                ) : (
                  <AnswerBlock value={item.answers[field.key]} field={field} />
                )}
              </div>
            ))}

            {orphanResume ? (
              <div className={styles.qa}>
                <p className={styles.question}>Resume</p>
                <Resume item={item} />
              </div>
            ) : null}

            {parts.retired.length > 0 ? (
              <>
                {/* Answers to questions the form has since dropped. Kept
                    visible rather than filtered out: somebody answered these,
                    and a panel that hides them is a panel that quietly loses
                    what was collected. */}
                <p className={styles.section}>No longer on this form</p>

                {parts.retired.map((key) => (
                  <div className={styles.qa} key={key}>
                    <p className={`${styles.question} ${styles.questionKey}`}>
                      {key}
                    </p>
                    <AnswerBlock value={item.answers[key]} field={null} />
                  </div>
                ))}
              </>
            ) : null}
          </>
        ) : null}
      </div>
    </>
  );
}

/**
 * The attached file, and a way to read it.
 *
 * The link is signed and stops working in about five minutes, which is why it
 * is fetched when this panel opens rather than when the table loaded. A panel
 * left open across a lunch break has a dead link in it; closing and reopening
 * mints a fresh one.
 */
function Resume({ item }: { item: ResponseItem }) {
  if (!item.resume) {
    return <p className={styles.unanswered}>Not answered</p>;
  }

  const { filename, sizeBytes, url } = item.resume;

  return (
    <p className={styles.resume}>
      <span className={styles.filename}>{filename}</span>
      <span className={styles.stamp}>{fileSize(sizeBytes)}</span>
      {url ? (
        <a className="button" href={url} rel="noopener">
          Download
        </a>
      ) : null}
    </p>
  );
}
