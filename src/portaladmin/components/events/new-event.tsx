"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useActionState, useEffect, useRef, useState } from "react";
import { Add01Icon, Cancel01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { createEvent } from "@/app/events/actions";
import styles from "./events-list.module.css";

/**
 * Starts an event, and goes into it.
 *
 * Two fields, because in the week an event is created two things are known: a
 * short name to file it under and a name to call it. Every date is months of
 * arguing away and the capacity depends on a room nobody has booked. Asking
 * for them here would mean either a form that is mostly empty or a set of
 * dates somebody made up to get past the screen, and a made-up date in this
 * table is indistinguishable from a real one.
 *
 * Creating lands on the dates screen for the new event, which is where the
 * rest of it gets filled in as it is settled.
 */
export function NewEvent() {
  const [open, setOpen] = useState(false);

  return <>
    <button type="button" className={`button primary ${styles.newButton}`} onClick={() => setOpen(true)}>
      <Icon icon={Add01Icon} size={18} />New event
    </button>
    {open ? <NewEventDialog onClose={() => setOpen(false)} /> : null}
  </>;
}

function NewEventDialog({ onClose }: { onClose: () => void }) {
  const [state, action, pending] = useActionState(createEvent, {});
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const dialog = useRef<HTMLDialogElement>(null);
  const nameInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    dialog.current?.showModal();
    nameInput.current?.focus();
  }, []);

  return (
    <dialog ref={dialog} className={styles.dialog} aria-labelledby="new-event-title" aria-describedby="new-event-description"
      onClose={onClose} onCancel={(event) => { if (pending) event.preventDefault(); }}>
      <form action={action}>
        <div className={styles.dialogHeader}>
          <h2 id="new-event-title">Create an event</h2>
          <button type="button" className={styles.closeButton} aria-label="Close new event" disabled={pending}
            onClick={() => dialog.current?.close()}><Icon icon={Cancel01Icon} size={20} /></button>
        </div>
        <p id="new-event-description" className={styles.dialogDescription}>Start with a name. You can set the dates, registration and capacity next.</p>
        <div className={styles.field}>
          <label htmlFor="event-name">Event name</label>
          <input ref={nameInput} id="event-name" name="name" required autoComplete="off" placeholder="e.g. MorganHacks 2028"
            value={name} onChange={(event) => setName(event.target.value)} disabled={pending} />
        </div>
        <div className={styles.field}>
          <label htmlFor="event-slug">Event slug</label>
          <input id="event-slug" name="slug" required autoComplete="off" spellCheck={false} placeholder="e.g. mh2028"
            minLength={2} maxLength={40} pattern="[a-zA-Z0-9]+(-[a-zA-Z0-9]+)*" className={styles.slugInput}
            value={slug} onChange={(event) => setSlug(event.target.value)} aria-describedby="event-slug-hint" disabled={pending} />
          <p id="event-slug-hint">A unique identifier for this event. Use letters, numbers and single hyphens. This cannot be changed later.</p>
        </div>
        {state.error ? <ErrorToast message={state.error} revision={state} /> : null}
        <div className={styles.dialogActions}>
          <button type="button" className={styles.secondaryButton} disabled={pending} onClick={() => dialog.current?.close()}>Cancel</button>
          <button type="submit" className={`button primary ${styles.newButton}`} disabled={pending || !name.trim() || !slug.trim()}>
            {pending ? "Creating…" : "Create event"}
          </button>
        </div>
      </form>
    </dialog>
  );
}
