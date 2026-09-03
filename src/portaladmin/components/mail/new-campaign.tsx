"use client";

import { useActionState, useState } from "react";
import type { FormState } from "@/app/mail/actions";
import styles from "./mail.module.css";
import { APPLICANT_STATUSES, type FormChoice } from "./types";

type Action = (state: FormState, form: FormData) => Promise<FormState>;

/**
 * Starting a campaign.
 *
 * Three decisions and no more: what it is called, which template it sends, and
 * who it goes to. Creating it sends nothing — it lands as a draft on its own
 * page, which is the only place a send can happen and only after a preview.
 *
 * The segment is one of three fixed shapes rather than a query builder. Comms
 * needs "everybody we accepted", "everybody who answered this form" and "these
 * nine addresses"; anything more expressive is another way to be wrong on the
 * screen where being wrong is several hundred emails nobody can recall.
 */
export function NewCampaign({
  forms,
  create,
}: {
  forms: FormChoice[];
  create: Action;
}) {
  const [state, action, pending] = useActionState(create, {});
  const [kind, setKind] = useState("applicants");

  return (
    <form action={action} className="panel">
      <h2>New campaign</h2>

      <div className="row">
        <div className="grow">
          <label htmlFor="name">Name</label>
          <input
            id="name"
            name="name"
            required
            autoComplete="off"
            className={styles.wide}
          />
        </div>

        <div>
          {/* The key, not the wording. A template's subject and body are edited
              where templates live; this screen only chooses which one goes. */}
          <label htmlFor="templateKey">Template key</label>
          <input
            id="templateKey"
            name="templateKey"
            required
            autoComplete="off"
            spellCheck={false}
          />
        </div>

        <div>
          <label htmlFor="segmentKind">Send to</label>
          <select
            id="segmentKind"
            name="segmentKind"
            value={kind}
            onChange={(event) => setKind(event.target.value)}
          >
            <option value="applicants">Applicants by status</option>
            <option value="form">Form respondents</option>
            <option value="addresses">Address list</option>
          </select>
        </div>
      </div>

      <div className={styles.segment}>
        {kind === "applicants" ? (
          <div>
            <label htmlFor="status">Status</label>
            <select id="status" name="status" defaultValue="accepted">
              {APPLICANT_STATUSES.map((status) => (
                <option key={status.value} value={status.value}>
                  {status.label}
                </option>
              ))}
            </select>
          </div>
        ) : null}

        {kind === "form" ? (
          <div>
            <label htmlFor="formId">Form</label>
            {forms.length === 0 ? (
              <p className="meta">No forms are available to you.</p>
            ) : (
              <select id="formId" name="formId" defaultValue="">
                <option value="" disabled>
                  Pick a form
                </option>
                {forms.map((form) => (
                  <option key={form.id} value={form.id}>
                    {form.name}
                  </option>
                ))}
              </select>
            )}
          </div>
        ) : null}

        {kind === "addresses" ? (
          <div>
            <label htmlFor="addresses">Addresses</label>
            <textarea
              id="addresses"
              name="addresses"
              spellCheck={false}
              autoComplete="off"
            />
            <p className="meta">One per line.</p>
          </div>
        ) : null}
      </div>

      <div className={styles.actions}>
        <button type="submit" className="button primary" disabled={pending}>
          {pending ? "Creating…" : "Create draft"}
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
