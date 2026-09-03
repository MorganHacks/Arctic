"use client";

import Link from "next/link";
import { useActionState, useState } from "react";
import type { FormState } from "@/app/mail/actions";
import type { TemplateRow } from "@/components/templates/types";
import styles from "./mail.module.css";
import {
  APPLICANT_STATUSES,
  type EventChoice,
  type FormChoice,
} from "./types";

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
  events,
  templates,
  templatesError,
  create,
}: {
  forms: FormChoice[];
  events: EventChoice[];
  /** Broadcast templates only. A campaign cannot send anything else. */
  templates: TemplateRow[];
  /** Why the templates could not be read, where they could not be. */
  templatesError: string | null;
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
              where templates live; this screen only chooses which one goes.

              Only the broadcast ones are here. A transactional template is
              refused at the send with a message about lanes and subdomains
              that reads like a fault in the system rather than a choice
              somebody made on this screen, so it is not offered at all. */}
          <label htmlFor="templateKey">Template</label>
          {templatesError !== null ? (
            <p className="meta">{templatesError}</p>
          ) : templates.length === 0 ? (
            <p className="meta">
              No broadcast templates. <Link href="/templates/new">Write one</Link>
            </p>
          ) : (
            <select id="templateKey" name="templateKey" required defaultValue="">
              <option value="" disabled>
                Pick a template
              </option>
              {templates.map((template) => (
                <option key={template.key} value={template.key}>
                  {template.key}
                </option>
              ))}
            </select>
          )}
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
            <label htmlFor="eventId">Event</label>
            {events.length === 0 ? (
              <span className="meta">There is no event yet.</span>
            ) : (
              <select id="eventId" name="eventId" defaultValue={events[0]?.id ?? ""}>
                {events.map((event) => (
                  <option key={event.id} value={event.id}>
                    {event.name}
                  </option>
                ))}
              </select>
            )}
          </div>
        ) : null}

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
          <div className={styles.addresses}>
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

        {/* For the case the dropdown cannot cover: none of the templates is
            the email somebody has in mind. Writing one is a screen away
            rather than a request to whoever has database access. */}
        {templates.length > 0 ? (
          <Link href="/templates/new" className="meta">
            Write a new template
          </Link>
        ) : null}
      </div>

      {state.error ? (
        <p className="error" style={{ marginTop: "0.9rem", marginBottom: 0 }}>
          {state.error}
        </p>
      ) : null}
    </form>
  );
}
