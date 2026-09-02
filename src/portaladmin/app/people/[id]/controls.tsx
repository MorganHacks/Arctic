"use client";

import { useActionState, useState } from "react";
import {
  grant,
  joinTeam,
  leaveTeam,
  revokePerson,
  ungrant,
  type FormState,
} from "../actions";

/** A team the person is on, as the screen needs it. */
export type TeamRow = {
  slug: string;
  name: string;
  expiresAt: string | null;
  expired: boolean;
};

/** One individual grant. */
export type GrantRow = {
  permission: string;
  expiresAt: string | null;
  expired: boolean;
  sensitive: boolean;
};

type Action = (state: FormState, form: FormData) => Promise<FormState>;

/**
 * A one-button form, for the removals.
 *
 * Its own component because each needs its own pending and error state, and a
 * shared one would report the failure of removing a grant against whichever
 * row happened to be first.
 */
function Remove({
  action,
  fields,
  label,
}: {
  action: Action;
  fields: Record<string, string>;
  label: string;
}) {
  const [state, submit, pending] = useActionState(action, {});

  return (
    <form action={submit}>
      {Object.entries(fields).map(([name, value]) => (
        <input key={name} type="hidden" name={name} value={value} />
      ))}
      <button type="submit" className="link" disabled={pending}>
        {pending ? "…" : label}
      </button>
      {state.error ? <span className="meta"> {state.error}</span> : null}
    </form>
  );
}

/** "until 2027-03-15", or why it is not counting. */
function Expiry({ expiresAt, expired }: { expiresAt: string | null; expired: boolean }) {
  if (!expiresAt) {
    return <span className="meta">no expiry</span>;
  }

  const day = expiresAt.slice(0, 10);

  return expired ? (
    <span className="pill lapsed">expired {day}</span>
  ) : (
    <span className="pill expiring">until {day}</span>
  );
}

export function Teams({
  personId,
  rows,
  available,
  canManage,
}: {
  personId: string;
  rows: TeamRow[];
  available: { slug: string; name: string }[];
  canManage: boolean;
}) {
  const [state, submit, pending] = useActionState(joinTeam, {});

  return (
    <section className="panel">
      <h2>Teams</h2>

      {rows.length === 0 ? (
        <p className="meta">On no teams, so the baselines grant them nothing.</p>
      ) : (
        <ul className="listing">
          {rows.map((team) => (
            <li key={team.slug}>
              <span>
                {team.name} <code>{team.slug}</code>
              </span>
              <span>
                <Expiry expiresAt={team.expiresAt} expired={team.expired} />
                {canManage ? (
                  <>
                    {" "}
                    <Remove
                      action={leaveTeam}
                      fields={{ id: personId, slug: team.slug }}
                      label="Remove"
                    />
                  </>
                ) : null}
              </span>
            </li>
          ))}
        </ul>
      )}

      {canManage && available.length > 0 ? (
        <form action={submit} className="row">
          <input type="hidden" name="id" value={personId} />

          <div>
            <label htmlFor="slug">Add to</label>
            <select id="slug" name="slug" defaultValue="">
              <option value="" disabled>
                Pick a team
              </option>
              {available.map((team) => (
                <option key={team.slug} value={team.slug}>
                  {team.name}
                </option>
              ))}
            </select>
          </div>

          <div>
            {/* Optional, and the reason it exists is the judge team: access
                that dies the day after the event rather than when somebody
                remembers. */}
            <label htmlFor="teamExpiry">Until (optional)</label>
            <input id="teamExpiry" name="expiresAt" type="date" />
          </div>

          <button type="submit" disabled={pending}>
            {pending ? "Adding…" : "Add"}
          </button>
        </form>
      ) : null}

      {state.error ? <p className="error">{state.error}</p> : null}
    </section>
  );
}

export function Grants({
  personId,
  rows,
  available,
  canGrant,
}: {
  personId: string;
  rows: GrantRow[];
  available: { value: string; sensitive: boolean }[];
  canGrant: boolean;
}) {
  const [state, submit, pending] = useActionState(grant, {});
  const [picked, setPicked] = useState("");

  const sensitive = available.some(
    (permission) => permission.value === picked && permission.sensitive,
  );

  return (
    <section className="panel">
      <h2>Individual grants</h2>
      <p className="meta" style={{ marginBottom: "0.75rem" }}>
        Layered on top of the team baselines. Additive only — there is no way to
        take a team&rsquo;s permission back from one person, and if they should
        not have it they should not be on that team.
      </p>

      {rows.length === 0 ? (
        <p className="meta">None. Everything they can do comes from a team.</p>
      ) : (
        <ul className="listing">
          {rows.map((row) => (
            <li key={row.permission}>
              <span>
                <code>{row.permission}</code>{" "}
                {row.sensitive ? <span className="pill sensitive">sensitive</span> : null}
              </span>
              <span>
                <Expiry expiresAt={row.expiresAt} expired={row.expired} />
                {canGrant ? (
                  <>
                    {" "}
                    <Remove
                      action={ungrant}
                      fields={{ id: personId, permission: row.permission }}
                      label="Remove"
                    />
                  </>
                ) : null}
              </span>
            </li>
          ))}
        </ul>
      )}

      {canGrant && available.length > 0 ? (
        <>
          <form action={submit} className="row">
            <input type="hidden" name="id" value={personId} />

            <div>
              <label htmlFor="permission">Grant</label>
              <select
                id="permission"
                name="permission"
                value={picked}
                onChange={(event) => setPicked(event.target.value)}
              >
                <option value="" disabled>
                  Pick a permission
                </option>
                {available.map((permission) => (
                  <option key={permission.value} value={permission.value}>
                    {permission.value}
                    {permission.sensitive ? " — sensitive" : ""}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label htmlFor="grantExpiry">Until (optional)</label>
              <input id="grantExpiry" name="expiresAt" type="date" />
            </div>

            <button type="submit" disabled={pending || picked === ""}>
              {pending ? "Granting…" : "Grant"}
            </button>
          </form>

          {/* The four that either move PII out of the system or change who is
              allowed to. Named rather than merely coloured, because the point
              is that somebody reads it before clicking. */}
          {sensitive ? (
            <p className="error" style={{ marginTop: "0.9rem" }}>
              This one is sensitive. Give it an expiry unless it genuinely needs
              to outlast the event.
            </p>
          ) : null}
        </>
      ) : null}

      {state.error ? <p className="error">{state.error}</p> : null}
    </section>
  );
}

/**
 * Revoking, behind a step somebody has to mean.
 *
 * A two-stage button rather than a browser confirm dialog: the confirmation
 * names the address, so the thing being read back is the person rather than
 * the verb. Revoking the wrong organizer during an event needs a second admin
 * to undo.
 */
export function Revoke({
  personId,
  email,
  isSelf,
}: {
  personId: string;
  email: string;
  isSelf: boolean;
}) {
  const [state, submit, pending] = useActionState(revokePerson, {});
  const [asked, setAsked] = useState(false);

  if (isSelf) {
    return (
      <section className="panel">
        <h2>Revoke access</h2>
        <p className="meta">
          You cannot revoke yourself. Ask another admin.
        </p>
      </section>
    );
  }

  return (
    <section className="panel">
      <h2>Revoke access</h2>
      <p className="meta" style={{ marginBottom: "0.75rem" }}>
        Takes them off the allowlist and ends every session they hold, including
        one open right now.
      </p>

      {asked ? (
        <form action={submit} className="row">
          <input type="hidden" name="id" value={personId} />
          <span>
            Revoke <strong>{email}</strong>?
          </span>
          <button type="submit" className="danger" disabled={pending}>
            {pending ? "Revoking…" : "Yes, revoke"}
          </button>
          <button type="button" onClick={() => setAsked(false)}>
            Cancel
          </button>
        </form>
      ) : (
        <button type="button" className="danger" onClick={() => setAsked(true)}>
          Revoke access
        </button>
      )}

      {state.error ? <p className="error">{state.error}</p> : null}
    </section>
  );
}
