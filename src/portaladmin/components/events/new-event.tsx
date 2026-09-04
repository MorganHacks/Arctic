"use client";

import { useActionState } from "react";
import { createEvent } from "@/app/events/actions";
import styles from "./events.module.css";

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
  const [state, action, pending] = useActionState(createEvent, {});

  return (
    <form action={action} className={styles.newEvent}>
      {/* The caveat beside the heading rather than under it. It is the one
          thing worth knowing before pressing Create, and a line of small print
          on its own row is a line that gets scrolled past. */}
      <div className={styles.newEventHead}>
        <h2>New event</h2>
        <p className={styles.newEventNote}>
          A slug and a name are enough. The dates and the capacity are set
          afterwards, as they are decided.
        </p>
      </div>

      <div className="row">
        <div className="field grow">
          <label htmlFor="slug">Slug</label>
          <input
            id="slug"
            name="slug"
            required
            autoComplete="off"
            spellCheck={false}
            className={styles.slugInput}
          />
          <p className="hint">It identifies the event and cannot be changed here.</p>
        </div>

        <div className="field grow">
          <label htmlFor="name">Name</label>
          <input
            id="name"
            name="name"
            required
            autoComplete="off"
            className={styles.nameInput}
          />
          <p className="hint">What the console calls this event on every screen.</p>
        </div>

      </div>

      {/* On its own line rather than beside the fields. Both fields carry a
          sentence under them, so a button in the row would sit level with the
          small print instead of with the boxes it submits. */}
      <div className={styles.createActions}>
        <button type="submit" className="button primary" disabled={pending}>
          {pending ? "Creating…" : "Create event"}
        </button>
      </div>

      {state.error ? (
        <p className="error" style={{ marginTop: "0.9rem", marginBottom: 0 }}>
          {state.error}
        </p>
      ) : null}
    </form>
  );
}
