"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useEffect, useRef, useState, useTransition } from "react";
import { Cancel01Icon, MailSend01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { sendTemplateTest } from "@/app/templates/actions";
import type { TemplateDraft } from "./types";
import styles from "./design-toolbar.module.css";

export function TestEmailDialog({ onClose, toRequest, defaultRecipient }: {
  onClose: () => void;
  toRequest: () => TemplateDraft;
  defaultRecipient: string;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const recipientInput = useRef<HTMLInputElement>(null);
  const request = useRef<{ fingerprint: string; id: string } | null>(null);
  const [recipient, setRecipient] = useState(defaultRecipient);
  const [error, setError] = useState<string | null>(null);
  const [queued, setQueued] = useState(false);
  const [sending, startSending] = useTransition();

  useEffect(() => {
    dialog.current?.showModal();
    recipientInput.current?.focus();
  }, []);

  return <dialog ref={dialog} className={styles.dialog} aria-labelledby="test-email-title"
    onClose={onClose} onCancel={(event) => { if (sending) event.preventDefault(); }}>
    <div className={styles.dialogHead}>
      <span className={styles.dialogIcon}><Icon icon={MailSend01Icon} size={22} /></span>
      <button type="button" className={styles.iconButton} aria-label="Close test email" disabled={sending} onClick={() => dialog.current?.close()}>
        <Icon icon={Cancel01Icon} size={18} />
      </button>
    </div>
    <h2 id="test-email-title">{queued ? "Test email queued" : "Send a test email"}</h2>
    {queued ? <>
      <p className={styles.success}>Your test is queued for <strong>{recipient.trim()}</strong>.</p>
      <div className={styles.dialogActions}><button type="button" className="button primary" onClick={() => dialog.current?.close()}>Done</button></div>
    </> : <form onSubmit={(event) => {
      event.preventDefault();
      const draft = toRequest();
      const fingerprint = JSON.stringify({ draft, recipient: recipient.trim() });
      if (request.current?.fingerprint !== fingerprint) request.current = { fingerprint, id: crypto.randomUUID() };
      const requestId = request.current.id;
      setError(null);
      startSending(async () => {
        const result = await sendTemplateTest(draft, recipient.trim(), requestId);
        if (result.ok) setQueued(true);
        else setError(result.error);
      });
    }}>
      <p className={styles.description}>Check your current design in an inbox before saving.</p>
      <label htmlFor="test-email-recipient">Send to</label>
      <input ref={recipientInput} id="test-email-recipient" type="email" autoComplete="email" required maxLength={320}
        placeholder="you@example.com" value={recipient} disabled={sending} onChange={(event) => { setRecipient(event.target.value); setError(null); }} />
      <p className={styles.hint}>Unfilled custom fields stay visible in the test email.</p>
      {error ? <ErrorToast message={error} /> : null}
      <div className={styles.dialogActions}>
        <button type="button" disabled={sending} onClick={() => dialog.current?.close()}>Cancel</button>
        <button type="submit" className="button primary" disabled={sending}>{sending ? "Queueing…" : "Send test email"}</button>
      </div>
    </form>}
  </dialog>;
}
