"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import { deleteTemplates } from "@/app/templates/actions";
import type { TemplateRow } from "./types";
import styles from "./design-toolbar.module.css";

export function DeleteTemplateDialog({ templates, protectedCount = 0, onClose, onDeleted }: {
  templates: TemplateRow[];
  protectedCount?: number;
  onClose: () => void;
  onDeleted: (keys: string[]) => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const cancel = useRef<HTMLButtonElement>(null);
  const [pending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);
  const multiple = templates.length > 1;

  useEffect(() => {
    dialog.current?.showModal();
    cancel.current?.focus();
  }, []);

  return <dialog ref={dialog} className={styles.dialog} aria-labelledby="delete-template-title"
    aria-describedby="delete-template-description" onClose={onClose}
    onCancel={(event) => { if (pending) event.preventDefault(); }}>
    <h2 id="delete-template-title">{multiple ? `Delete ${templates.length} templates?` : "Delete template?"}</h2>
    <p id="delete-template-description" className={styles.description}>
      {multiple ? "These templates" : <strong>{templates[0].name || templates[0].key}</strong>} will be removed for everyone.
      {templates.some((template) => template.version > 0)
        ? " Existing campaigns and queued or sent emails will be kept. Draft campaigns using these templates can no longer be sent."
        : " This will discard your unpublished drafts."}
    </p>
    {multiple ? <ul className={styles.description}>{templates.map((template) => (
      <li key={template.key}>{template.name || template.key}</li>
    ))}</ul> : null}
    {protectedCount > 0 ? <p className={styles.description}>
      {protectedCount} account-access {protectedCount === 1 ? "template is" : "templates are"} protected and won’t be deleted.
    </p> : null}
    {error ? <p className={styles.error} role="alert">{error}</p> : null}
    <div className={styles.dialogActions}>
      <button ref={cancel} type="button" disabled={pending} onClick={() => dialog.current?.close()}>Cancel</button>
      <button type="button" className="button danger" disabled={pending} onClick={() => {
        setError(null);
        startTransition(async () => {
          try {
            const result = await deleteTemplates(templates.map(({ key, version }) => ({ key, version })));
            if (result.error) setError(result.error);
            else if (result.failed.length > 0) {
              const errors = [...new Set(result.failed.map((item) => item.error))].join(" ");
              setError(`${result.failed.length} ${result.failed.length === 1 ? "template could" : "templates could"} not be deleted. ${errors}`);
            }
            if (result.deleted.length > 0) onDeleted(result.deleted);
          } catch {
            setError("The templates could not be deleted. Try again.");
          }
        });
      }}>{pending ? "Deleting…" : multiple ? "Delete templates" : "Delete template"}</button>
    </div>
  </dialog>;
}
