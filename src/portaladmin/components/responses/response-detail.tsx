"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useEffect, useRef } from "react";
import { Cancel01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import type { FormField } from "@/lib/api";
import { AnswerBlock, fileSize } from "./answers";
import { askedAndRetired } from "./columns";
import { ResponsePerson } from "./response-person";
import { submittedAt } from "./respondent";
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
  const panel = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = panel.current;
    const previous = document.activeElement;
    dialog?.showModal();
    return () => {
      dialog?.close();
      if (previous instanceof HTMLElement && previous.isConnected) previous.focus();
    };
  }, []);

  const parts = item ? askedAndRetired(fields, item) : null;
  const submitted = item ? submittedAt(item.submittedAt) : null;

  // A resume with no file question left to hang it under. The question was
  // deleted after somebody uploaded; the file is still theirs and still there.
  const orphanResume =
    item?.resume != null && !fields.some((field) => field.type === "file");

  return (
      <dialog
        className={styles.panel}
        aria-label="Response"
        ref={panel}
        onCancel={onClose}
        onClick={(event) => {
          if (event.target !== event.currentTarget) return;
          const rect = event.currentTarget.getBoundingClientRect();
          if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) onClose();
        }}
      >
        <div className={styles.panelHead}>
          <h2>Response details</h2>
          <button type="button" className={styles.iconButton} onClick={onClose} aria-label="Close response">
            <Icon icon={Cancel01Icon} size={18} />
          </button>
        </div>

        <div className={styles.panelBody}>
        {loading ? <div className={styles.loadingDetail} role="status" aria-label="Loading response"><span /><span /><span /></div> : null}
        <ErrorToast message={error} />

        {item && parts ? (
          <>
            <div className={styles.detailPerson}>
              <ResponsePerson item={item} fields={fields} />
              <div className={styles.detailMeta}>
                <time dateTime={item.submittedAt}>{submitted?.date} at {submitted?.time}</time>
                <span aria-hidden="true">·</span><span>Version {item.formVersion}</span>
              </div>
            </div>
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
      </dialog>
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
