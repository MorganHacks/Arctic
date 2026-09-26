"use client";

import { useActionState, useEffect, useRef, useState } from "react";
import { Add01Icon, Cancel01Icon, ClipboardListIcon, FormIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import styles from "@/components/formslist/forms-page.module.css";
import { createForm } from "./actions";

/**
 * Starts a form and goes straight into it.
 *
 * Only a name and a kind, because everything else about a form is a question
 * on it, and questions are what the builder is for. The kind is here rather
 * than in the builder because it cannot be changed afterwards — an application
 * form is the one that creates an applicant, and a survey that quietly became
 * one would be a mess nobody could untangle.
 */
export function NewForm({ eventId, eventName, hasApplication, disabled }: {
  eventId: string; eventName: string; hasApplication: boolean; disabled?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    function fromHash() { if (window.location.hash === "#new-form") setOpen(true); }
    function fromLink(event: MouseEvent) {
      if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || disabled) return;
      const link = event.target instanceof Element ? event.target.closest<HTMLAnchorElement>("a[href]") : null;
      if (!link) return;
      const target = new URL(link.href);
      if (target.origin === window.location.origin && target.pathname === window.location.pathname
        && target.search === window.location.search && target.hash === "#new-form") {
        event.preventDefault(); setOpen(true);
      }
    }
    fromHash();
    window.addEventListener("hashchange", fromHash);
    document.addEventListener("click", fromLink, true);
    return () => { window.removeEventListener("hashchange", fromHash); document.removeEventListener("click", fromLink, true); };
  }, [disabled]);

  function close() {
    setOpen(false);
    if (window.location.hash === "#new-form") window.history.replaceState(window.history.state, "", window.location.pathname + window.location.search);
    trigger.current?.focus();
  }

  return <>
    <button ref={trigger} id="new-form" type="button" className={styles.primaryButton} disabled={disabled} onClick={() => setOpen(true)}><Icon icon={Add01Icon} size={18} />New form</button>
    {open ? <NewFormDialog eventId={eventId} eventName={eventName} hasApplication={hasApplication} onClose={close} /> : null}
  </>;
}

function NewFormDialog({ eventId, eventName, hasApplication, onClose }: {
  eventId: string; eventName: string; hasApplication: boolean; onClose: () => void;
}) {
  const [state, action, pending] = useActionState(createForm, {});
  const [name, setName] = useState("");
  const [kind, setKind] = useState("survey");
  const dialog = useRef<HTMLDialogElement>(null);
  const nameInput = useRef<HTMLInputElement>(null);

  useEffect(() => { dialog.current?.showModal(); nameInput.current?.focus(); }, []);

  return <dialog ref={dialog} className={styles.dialog} aria-labelledby="new-form-title" aria-describedby="new-form-description"
    onClose={onClose} onCancel={event => { if (pending) event.preventDefault(); }}>
    <form action={action}>
      <div className={styles.dialogHeader}><h2 id="new-form-title">New form</h2><button type="button" className={styles.closeButton} aria-label="Close new form" disabled={pending} onClick={() => dialog.current?.close()}><Icon icon={Cancel01Icon} size={20} /></button></div>
      <p id="new-form-description" className={styles.dialogDescription}>Create a form for <strong>{eventName}</strong>.</p>
      <input type="hidden" name="eventId" value={eventId} />
      <div className={styles.field}>
        <label htmlFor="form-name">Form name</label>
        <input ref={nameInput} id="form-name" name="name" required autoComplete="off" placeholder="e.g. Mentor sign-up" value={name} onChange={event => setName(event.target.value)} disabled={pending} />
      </div>
      <fieldset className={styles.formTypes} disabled={pending}>
        <legend>Form type</legend>
        <label className={styles.typeChoice} data-kind="survey">
          <Icon icon={ClipboardListIcon} size={22} />
          <span><strong>Survey</strong><small>Sign-ups, feedback and custom questions.</small></span>
          <input type="radio" name="kind" value="survey" checked={kind === "survey"} onChange={() => setKind("survey")} />
        </label>
        <label className={styles.typeChoice} data-kind="application" data-disabled={hasApplication}>
          <Icon icon={FormIcon} size={22} />
          <span><strong>Application</strong><small>{hasApplication ? "This event already has an application form." : "Starts with standard applicant questions. One per event."}</small></span>
          <input type="radio" name="kind" value="application" checked={kind === "application"} onChange={() => setKind("application")} disabled={hasApplication || pending} />
        </label>
      </fieldset>
      <p className={styles.formHint}>The form type cannot be changed later.</p>
      {state.error ? <p className={styles.error} role="alert">{state.error}</p> : null}
      <div className={styles.dialogActions}>
        <button type="button" className={styles.secondaryButton} disabled={pending} onClick={() => dialog.current?.close()}>Cancel</button>
        <button type="submit" className={styles.primaryButton} disabled={pending || !name.trim()}>{pending ? "Creating…" : "Create form"}</button>
      </div>
    </form>
  </dialog>;
}
