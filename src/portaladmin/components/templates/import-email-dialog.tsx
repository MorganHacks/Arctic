"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useEffect, useRef, useState, useTransition } from "react";
import { Cancel01Icon, Link04Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { importTemplateHtml } from "@/app/templates/actions";
import styles from "./design-toolbar.module.css";

export function ImportEmailDialog({ hasContent, onClose, onImport }: {
  hasContent: boolean;
  onClose: () => void;
  onImport: (body: string) => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const urlInput = useRef<HTMLInputElement>(null);
  const [url, setUrl] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [importing, startImporting] = useTransition();

  useEffect(() => {
    dialog.current?.showModal();
    urlInput.current?.focus();
  }, []);

  return <dialog ref={dialog} className={styles.dialog} aria-labelledby="import-email-title"
    onClose={onClose} onCancel={(event) => { if (importing) event.preventDefault(); }}>
    <div className={styles.dialogHead}>
      <span className={styles.dialogIcon}><Icon icon={Link04Icon} size={22} /></span>
      <button type="button" className={styles.iconButton} aria-label="Close import" disabled={importing} onClick={() => dialog.current?.close()}>
        <Icon icon={Cancel01Icon} size={18} />
      </button>
    </div>
    <h2 id="import-email-title">Import from URL</h2>
    <form onSubmit={(event) => {
      event.preventDefault();
      setError(null);
      startImporting(async () => {
        const result = await importTemplateHtml(url.trim());
        if (!result.ok) { setError(result.error); return; }
        onImport(result.body);
        dialog.current?.close();
      });
    }}>
      <p className={styles.description}>Bring an existing HTML email into your design.</p>
      <label htmlFor="import-email-url">Email URL</label>
      <input ref={urlInput} id="import-email-url" type="url" autoComplete="url" required maxLength={2048}
        placeholder="https://example.com/email.html" value={url} disabled={importing}
        onChange={(event) => { setUrl(event.target.value); setError(null); }} />
      <p className={styles.hint}>{hasContent
        ? "Importing replaces your current content with the HTML from this public URL."
        : "Use a public URL that opens your email’s HTML."}</p>
      {error ? <ErrorToast message={error} /> : null}
      <div className={styles.dialogActions}>
        <button type="button" disabled={importing} onClick={() => dialog.current?.close()}>Cancel</button>
        <button type="submit" className="button primary" disabled={importing}>
          {importing ? "Importing…" : hasContent ? "Replace and import" : "Import HTML"}
        </button>
      </div>
    </form>
  </dialog>;
}
