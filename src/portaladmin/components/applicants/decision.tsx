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
}: {
  id: string;
  allowedNext: Status[];
}) {
  const [state, submit, pending] = useActionState(changeStatus, {});

  if (allowedNext.length === 0) {
    return (
      <p className="meta">
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
        <select id="status" name="status" defaultValue="" style={{ width: "100%" }}>
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
