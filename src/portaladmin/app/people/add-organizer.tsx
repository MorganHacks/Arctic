"use client";

import Link from "next/link";
import { useActionState } from "react";
import { addOrganizer } from "./actions";

/**
 * Adds an address to the allowlist.
 *
 * There is no team picker here, and that is not an omission. A new organizer
 * lands with nothing until somebody decides what they should have, so the form
 * hands off to their page rather than pretending the decision can be made in
 * the same keystroke as the address.
 */
export function AddOrganizer() {
  const [state, action, pending] = useActionState(addOrganizer, {});

  return (
    <form action={action} className="panel">
      <h2>Add an organizer</h2>
      <p className="meta" style={{ marginBottom: "0.75rem" }}>
        They sign in with this Google account. Being on the list grants nothing
        on its own.
      </p>

      <div className="row">
        <div className="grow">
          <label htmlFor="email">Email</label>
          <input
            id="email"
            name="email"
            type="email"
            required
            autoComplete="off"
            placeholder="name@morgan.edu"
            style={{ width: "100%" }}
          />
        </div>
        <button type="submit" className="button primary" disabled={pending}>
          {pending ? "Adding…" : "Add"}
        </button>
      </div>

      {state.error ? (
        <p className="error" style={{ marginTop: "0.9rem", marginBottom: 0 }}>
          {state.error}
          {/* The one refusal with somewhere to go. Typing the address of a
              revoked colleague back into this box is the obvious thing to try
              and cannot work, so the refusal carries the page that can. */}
          {state.personId ? (
            <>
              {" "}
              <Link href={`/people/${state.personId}`}>Open their page</Link>
            </>
          ) : null}
        </p>
      ) : null}
    </form>
  );
}
