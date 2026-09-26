"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useEffect, useId, useRef, useState } from "react";
import { Cancel01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import styles from "@/components/formslist/forms-page.module.css";
import { unpublishForm } from "../actions";

export function Unpublish({ formId, formName, onClose, onDone }: {
  formId: string;
  formName: string;
  onClose: () => void;
  onDone: () => void;
}) {
  const id = useId();
  const dialog = useRef<HTMLDialogElement>(null);
  const cancel = useRef<HTMLButtonElement>(null);
  const [working, setWorking] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    dialog.current?.showModal();
    cancel.current?.focus();
  }, []);

  async function run() {
    if (working) return;
    setWorking(true);
    setNotice(null);

    try {
      const result = await unpublishForm(formId);
      if (!result.ok) {
        setNotice(result.error ?? "This form could not be unpublished.");
        return;
      }
      dialog.current?.close();
      onDone();
    } catch {
      setNotice("This form could not be unpublished. Please try again.");
    } finally {
      setWorking(false);
    }
  }

  return <dialog ref={dialog} className={styles.dialog} aria-labelledby={`${id}-title`}
    aria-describedby={`${id}-description`} onClose={onClose}
    onCancel={event => { if (working) event.preventDefault(); }}>
    <form onSubmit={event => { event.preventDefault(); void run(); }}>
      <div className={styles.dialogHeader}>
        <h2 id={`${id}-title`}>Unpublish form?</h2>
        <button type="button" className={styles.closeButton} aria-label="Close dialog" disabled={working}
          onClick={() => dialog.current?.close()}><Icon icon={Cancel01Icon} size={20} /></button>
      </div>
      <p id={`${id}-description`} className={styles.dialogDescription}>
        <strong>{formName}</strong> will stop accepting responses. Existing responses and your draft
        will be kept. You can publish it again anytime.
      </p>
      <ErrorToast message={notice} />
      <div className={styles.dialogActions}>
        <button ref={cancel} type="button" className={styles.secondaryButton} disabled={working}
          onClick={() => dialog.current?.close()}>Cancel</button>
        <button type="submit" className={styles.primaryButton} disabled={working}>
          {working ? "Unpublishing…" : "Unpublish"}
        </button>
      </div>
    </form>
  </dialog>;
}
