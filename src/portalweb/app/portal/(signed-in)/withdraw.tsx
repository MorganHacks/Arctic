"use client";

import { useActionState, useEffect, useRef } from "react";
import type { Withdrawal } from "@/lib/api";
import { withdrawApplication } from "../actions";

/**
 * Telling us you are not coming.
 *
 * The last thing on the status page, quietly, because it is the one control
 * here that nobody should reach for by accident and everybody should be able
 * to find when they need it. Those two pull in opposite directions and the
 * resolution is not to hide it: an applicant who cannot find this writes to an
 * organizer instead, and the seat stays held either way until somebody gets
 * round to the inbox.
 *
 * The press does not withdraw anything. It opens a dialog that says plainly
 * what happens, and the withdrawal is the second press inside it — modal
 * rather than the inline second ask the RSVP panel uses, because that one is
 * an answer somebody came to the page to give and this one interrupts whatever
 * they were doing. A native `<dialog>` opened with `showModal` is what makes
 * that honest for everybody: the browser traps focus inside it, closes it on
 * Escape, puts focus back where it was afterwards, and makes the rest of the
 * page inert to a screen reader. Every hand-rolled version of this gets at
 * least one of those wrong, usually the last two.
 *
 * There is deliberately no "is it open" state in this component. The obvious
 * shape — a boolean, an effect that calls showModal or close to match it — has
 * one state that cannot recover: if the dialog closes without React hearing
 * about it, the boolean stays true, the next press sets true to true, nothing
 * re-renders, and the button is dead for the rest of the session with no error
 * anywhere. That was not hypothetical here; the close event did not arrive.
 * Opening and closing the element directly has no second copy of the truth to
 * disagree with the first.
 *
 * Nothing here is a gate. The API refuses a withdrawal the lifecycle does not
 * allow whatever this renders, so `open` is a courtesy — the same relationship
 * the RSVP panel has with `rsvp.open` and the profile form has with
 * `profileEditable`. What this component owes the applicant is not enforcement.
 * It is being sure they meant it.
 *
 * @param withdraw Whether the API would accept a withdrawal, and the sentence
 *   explaining it if not.
 */
export function WithdrawPanel({ withdraw }: { withdraw: Withdrawal }) {
  const [state, action, pending] = useActionState(withdrawApplication, {});
  const dialog = useRef<HTMLDialogElement>(null);
  const keep = useRef<HTMLButtonElement>(null);

  // The write landed. The panel is about to be replaced by the closed sentence
  // anyway, but closing here means the dialog is never the thing left holding
  // focus while the page redraws underneath it.
  useEffect(() => {
    if (state.done === true) {
      dialog.current?.close();
    }
  }, [state.done]);

  if (!withdraw.open) {
    return (
      <section className="panel withdraw" aria-labelledby="withdraw-heading">
        <h2 id="withdraw-heading">Withdraw your application</h2>
        <p className="quiet" style={{ marginBottom: 0 }}>
          {withdraw.closedReason}
        </p>
      </section>
    );
  }

  return (
    <section className="panel withdraw" aria-labelledby="withdraw-heading">
      <h2 id="withdraw-heading">Withdraw your application</h2>
      <p className="quiet">
        If your plans have changed, tell us here rather than going quiet.
        Withdrawing closes your application: we stop reviewing it, we stop
        emailing you about the event, and any spot you are holding goes to
        somebody on the waitlist.
      </p>

      {/*
        The same failure is rendered here and inside the dialog, rather than in
        whichever one happens to be in front. A modal dialog makes the rest of
        the page inert, so only one of the two is ever readable — and neither
        can be the one that goes missing because a piece of state said the
        dialog was somewhere it was not.
      */}
      {state.error !== undefined ? (
        <div className="notice problem">
          <p>{state.error}</p>
        </div>
      ) : null}

      <div className="actions">
        {/*
          Deliberately not a submit, and deliberately not the accent colour.
          Nothing has happened yet when this is pressed, and the press that
          starts something irreversible should cost nothing.

          showModal() rather than an `open` prop, which is the whole point of
          using this element: `<dialog open>` renders an ordinary box with no
          top layer, no focus trap, no Escape and no backdrop.
        */}
        <button
          type="button"
          onClick={() => {
            const element = dialog.current;
            if (element !== null && !element.open) {
              element.showModal();

              // showModal() would otherwise land on the first thing it can
              // focus, which is the button that ends the application.
              // Somebody who opened this by mistake and pressed space to get
              // rid of it would withdraw instead, and the whole point of the
              // second ask is that the second press has to be aimed.
              keep.current?.focus();
            }
          }}
        >
          Withdraw my application
        </button>
      </div>

      <dialog
        ref={dialog}
        className="ask"
        aria-labelledby="withdraw-sure"
        /*
         * Escape fires this before it closes. Cancelled while the request is
         * in flight, because a dialog that vanishes mid-write leaves somebody
         * looking at a page that has not changed yet with no idea whether they
         * withdrew.
         */
        onCancel={(event) => {
          if (pending) {
            event.preventDefault();
          }
        }}
      >
        <h2 id="withdraw-sure">Withdraw your application?</h2>
        <p>
          This closes your application for good. If you are holding a spot it
          goes to somebody on the waitlist, and we stop emailing you about the
          event.
        </p>
        <p>
          You cannot undo this here. Email us if you change your mind and an
          organizer will take a look.
        </p>

        {state.error !== undefined ? (
          <div className="notice problem">
            <p>{state.error}</p>
          </div>
        ) : null}

        <form action={action}>
          <div className="actions">
            <button type="submit" className="danger" disabled={pending}>
              {pending ? "Withdrawing…" : "Yes, withdraw my application"}
            </button>
            {/*
              Second in the markup because the dialog is read top to bottom and
              the destructive button is what it is about. Focus starts here
              rather than there, which the opening handler does by hand.
            */}
            <button
              ref={keep}
              type="button"
              className="link"
              onClick={() => dialog.current?.close()}
              disabled={pending}
            >
              Keep my application
            </button>
          </div>
        </form>
      </dialog>
    </section>
  );
}
