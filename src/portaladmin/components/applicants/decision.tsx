"use client";

import { useActionState } from "react";
import { changeStatus } from "@/app/applicants/actions";
import styles from "./applicants.module.css";
import { label } from "./status";
import type { Status } from "./types";

/**
 * Moving an applicant to a new status.
 *
 * The menu is built from `allowedNext`, which the API works out from the
 * lifecycle table it already has. Not a copy of that table over here: a
 * console that offered a move the API refuses would be a button whose only
 * outcome is an error message, and a console that hid a legal one would be a
 * decision nobody can make.
 *
 * That is still only a courtesy. Two reviewers on the same applicant is
 * ordinary — that is what a shared queue is — so the record can be out of date
 * by the time this is pressed, and the API refuses the move against what is
 * actually true rather than against what this screen last saw. The 409 that
 * comes back then is the system working, and it arrives here as a sentence
 * saying where the application actually is.
 *
 * The reason goes onto an append-only history row that can never be edited,
 * which is why it is optional and why it is short. Anything that wants
 * revising belongs in a note.
 */
export function Decision({
  id,
  allowedNext,
  canDecide,
}: {
  id: string;
  allowedNext: Status[];

  /**
   * Whether this reader holds `applications.decide`.
   *
   * `allowedNext` does not answer this and should not: it describes the
   * application, not the reader, and the same lifecycle is true whoever is
   * looking at it. Two different reasons for there being no button — the
   * record has nowhere to go, and this person may not move it — need two
   * different sentences, and only one of them is worth asking an admin about.
   */
  canDecide: boolean;
}) {
  const [state, submit, pending] = useActionState(changeStatus, {});

  if (!canDecide) {
    return (
      <p className={styles.refusal}>
        You do not have <code>applications.decide</code>. Ask an admin.
      </p>
    );
  }

  if (allowedNext.length === 0) {
    return (
      <p className={styles.terminal}>
        Nowhere left to go. Reversing this would be a new application rather
        than an edit, so that the history keeps saying what happened.
      </p>
    );
  }

  return (
    <form action={submit} className={styles.decide}>
      <input type="hidden" name="id" value={id} />

      <div>
        <label htmlFor="status">Move to</label>
        <select id="status" name="status" defaultValue="">
          <option value="" disabled>
            Pick a status
          </option>
          {allowedNext.map((status) => (
            <option key={status} value={status}>
              {label(status)}
            </option>
          ))}
        </select>
      </div>

      <div>
        <label htmlFor="reason">Reason (optional)</label>
        <textarea id="reason" name="reason" maxLength={500} />
      </div>

      <div>
        <button type="submit" className="button primary" disabled={pending}>
          {pending ? "Saving…" : "Change status"}
        </button>
      </div>

      {state.error ? <p className="error">{state.error}</p> : null}
    </form>
  );
}
