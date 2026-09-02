"use client";

import { useActionState } from "react";
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
export function NewForm({ eventId }: { eventId: string }) {
  const [state, action, pending] = useActionState(createForm, {});

  return (
    <form action={action} className="panel">
      <h2>New form</h2>
      <p className="meta" style={{ marginBottom: "0.75rem" }}>
        An application form starts with MLH&rsquo;s required questions already
        on it, and there can only be one per event.
      </p>

      <div className="row">
        <input type="hidden" name="eventId" value={eventId} />

        <div className="grow">
          <label htmlFor="name">Name</label>
          <input
            id="name"
            name="name"
            required
            autoComplete="off"
            placeholder="Mentor sign-up"
            style={{ width: "100%" }}
          />
        </div>

        <div>
          <label htmlFor="kind">Kind</label>
          <select id="kind" name="kind" defaultValue="survey">
            <option value="survey">Survey</option>
            <option value="application">Application</option>
          </select>
        </div>

        <button type="submit" className="button primary" disabled={pending}>
          {pending ? "Creating…" : "Create"}
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
