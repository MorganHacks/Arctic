"use client";

import { useEffect, useRef, useState } from "react";
import { unpublishForm } from "../actions";
import styles from "./builder.module.css";

/**
 * Taking a live form down.
 *
 * Destructive in the way that actually matters, which is not that a row
 * disappears. The code on a flyer stops resolving the moment this returns, and
 * nobody holding that flyer finds out except by trying and failing. Publishing
 * again brings the link back; it does not bring back the hour in which nobody
 * could answer, and nobody will ever report it.
 *
 * So it is dressed the way the console dresses its other irreversible control:
 * --stop rather than the accent, because the accent means "the thing to do
 * next" and this is not that, and two presses rather than one.
 *
 * The confirmation names the form. "Unpublish" twice tells nobody whether they
 * are on the right screen, and this console is one where somebody has four form
 * tabs open. The question takes focus when it appears, so somebody hearing the
 * page is told what they are being asked rather than left with a button that
 * silently changed meaning, and so a held Enter cannot make both presses.
 *
 * What it says about answers is the point of the copy. The fear this control
 * provokes is that four hundred applications are about to go, and an organizer
 * who is not told otherwise simply does not press it — which leaves a wrong
 * form live because the control that fixes it looked too frightening to use.
 */
export function Unpublish({
  formId,
  formName,
  onDone,
}: {
  formId: string;
  formName: string;

  /** Pulls the new state down, so the pill in the header stops saying Live. */
  onDone: () => void;
}) {
  const [asked, setAsked] = useState(false);
  const [working, setWorking] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const question = useRef<HTMLParagraphElement>(null);

  useEffect(() => {
    if (asked) {
      question.current?.focus();
    }
  }, [asked]);

  async function run() {
    setWorking(true);
    setNotice(null);

    const result = await unpublishForm(formId);

    setWorking(false);

    if (!result.ok) {
      setNotice(result.error ?? "That did not work.");
      return;
    }

    setAsked(false);
    onDone();
  }

  return (
    <section className={`panel ${styles.stop}`}>
      <h2>Unpublish</h2>
      <p className="meta">
        Takes this form back to draft. The link stops working straight away,
        including one already printed on a flyer. Answers already given are
        kept, and the responses screen goes on working.
      </p>

      {asked ? (
        <div className={styles.confirm}>
          <p className={styles.asking} ref={question} tabIndex={-1}>
            Unpublish <strong>{formName}</strong>?
          </p>
          <button
            type="button"
            className="danger"
            disabled={working}
            onClick={() => void run()}
          >
            {working ? "Unpublishing…" : "Yes, unpublish"}
          </button>
          <button type="button" disabled={working} onClick={() => setAsked(false)}>
            Cancel
          </button>
        </div>
      ) : (
        <button type="button" className="danger" onClick={() => setAsked(true)}>
          Unpublish
        </button>
      )}

      {notice ? <p className="error">{notice}</p> : null}
    </section>
  );
}
