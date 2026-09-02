"use client";

import { useActionState } from "react";
import { requestLink } from "../actions";

/**
 * Asks for a sign-in link.
 *
 * The confirmation is the same sentence whether or not the address belongs to
 * anybody, and this component must never grow a branch that changes it. "No
 * account found" for one address and "check your inbox" for another turns this
 * form into a way to ask us who applied to the hackathon, and the API answering
 * identically counts for nothing if the screen in front of it does not.
 *
 * Which is why the confirmation and the genuine errors share one slot: there
 * is one place a message can appear, and one string that fills it after a
 * successful submit.
 */
export function SignInForm() {
  const [state, action, pending] = useActionState(requestLink, {});

  if (state.done) {
    return (
      <div className="notice done">
        <p>{state.message}</p>
        <p className="quiet" style={{ marginBottom: 0 }}>
          Check your spam folder if it is not there in a couple of minutes.
        </p>
      </div>
    );
  }

  return (
    <form action={action}>
      {state.error ? (
        <div className="notice problem">
          <p>{state.error}</p>
        </div>
      ) : null}

      <div className="field">
        <label htmlFor="email">Email address</label>
        <input
          id="email"
          name="email"
          type="email"
          required
          autoComplete="email"
          // No autofocus. On a phone it opens the keyboard over the page
          // before the person has read what the page is for.
          placeholder="you@example.com"
        />
      </div>

      <div className="actions">
        <button type="submit" className="primary" disabled={pending}>
          {pending ? "Sending…" : "Email me a link"}
        </button>
      </div>
    </form>
  );
}
