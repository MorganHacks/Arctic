"use client";

import { useActionState, useState } from "react";
import type { Rsvp } from "@/lib/api";
import { answerRsvp } from "../actions";

/**
 * Taking a spot, or giving it back.
 *
 * The one screen in this portal where somebody does something consequential,
 * so it is built around the two ways it goes wrong. Confirming is the thing
 * they came to do and gets the accent and the whole width. Declining is quiet,
 * sits below, and asks a second time, because it releases the spot and the
 * lifecycle has no route back from it: an application that declines is
 * terminal, and the place has gone to the next person on the waitlist by the
 * time anybody notices the mistake.
 *
 * Nothing here is a gate. The API refuses a confirm that is out of time or out
 * of status whatever this renders, so `open` is a courtesy — the same
 * relationship the profile form has with `editable`. What this component owes
 * the applicant is not enforcement; it is knowing which of the two they are
 * about to do, and by when.
 *
 * @param rsvp Whether there is anything to answer, and the deadline if so.
 * @param deadline The deadline already rendered in the event's zone, or null.
 *   Formatted on the server so one date cannot be shown two ways, and so the
 *   markup the browser receives is the markup it keeps.
 */
export function RsvpPanel({
  rsvp,
  deadline,
}: {
  rsvp: Rsvp;
  deadline: string | null;
}) {
  const [state, action, pending] = useActionState(answerRsvp, {});
  const [confirmingDecline, setConfirmingDecline] = useState(false);

  // The window has gone but the row has not been expired yet, so the status
  // line above still reads "confirm by". Said plainly rather than left to be
  // inferred from two buttons that are missing.
  if (!rsvp.open) {
    return rsvp.closedReason !== null && deadline !== null ? (
      <section className="panel" aria-label="Your spot">
        <h2>Your spot</h2>
        <p className="quiet" style={{ marginBottom: 0 }}>
          {rsvp.closedReason}
        </p>
      </section>
    ) : null;
  }

  return (
    <section className="panel rsvp" aria-label="Your spot">
      <h2>Your spot</h2>

      {deadline !== null ? (
        <p className="rsvp__by">Confirm by {deadline}</p>
      ) : null}

      {state.error ? (
        <div className="notice problem">
          <p>{state.error}</p>
        </div>
      ) : null}

      <form action={action}>
        {confirmingDecline ? (
          /*
           * aria-live, because this replaces the controls under the pointer
           * rather than adding to them. A sighted person sees the swap; a
           * screen reader would otherwise be told nothing until the next time
           * something moved focus.
           */
          <div className="rsvp__sure" aria-live="polite">
            <p>Declining releases your spot. This cannot be undone.</p>
            <div className="actions">
              <button
                type="submit"
                name="answer"
                value="decline"
                className="danger"
                disabled={pending}
              >
                {pending ? "Saving…" : "Yes, decline my spot"}
              </button>
              <button
                type="button"
                className="link"
                onClick={() => setConfirmingDecline(false)}
                disabled={pending}
              >
                Keep my spot
              </button>
            </div>
          </div>
        ) : (
          <div className="actions">
            <button
              type="submit"
              name="answer"
              value="confirm"
              className="primary"
              disabled={pending}
            >
              {pending ? "Saving…" : "Confirm your spot"}
            </button>
            {/*
              Deliberately not a submit. Declining is one press away from
              permanent, and the press that starts it should cost nothing.
            */}
            <button
              type="button"
              className="link"
              onClick={() => setConfirmingDecline(true)}
              disabled={pending}
            >
              I cannot make it
            </button>
          </div>
        )}
      </form>
    </section>
  );
}
