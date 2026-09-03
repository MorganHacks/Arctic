"use client";

import { useState } from "react";
import { saveAudience } from "../actions";
import styles from "./builder.module.css";

/**
 * Who this form is for.
 *
 * Beside the questions rather than on a settings screen of its own, because it
 * is a decision made while building the form and not one somebody goes looking
 * for afterwards — an RSVP is for accepted applicants from the moment it is a
 * thought, and a feedback survey for people who turned up.
 *
 * Not part of the autosave. Every other control on this screen writes as
 * somebody types, which is right for wording and wrong for a door: narrowing an
 * audience closes a live form to people who were about to answer it, and a
 * half-typed intention should not do that. So this is an explicit press.
 */
export function Audience({
  formId,
  kind,
  statuses,
  initialRequiresSignIn,
  initialStatuses,
  canManage,
}: {
  formId: string;
  kind: string;
  /** Every status there is, from the server. */
  statuses: string[];
  initialRequiresSignIn: boolean;
  initialStatuses: string[];
  canManage: boolean;
}) {
  const [gated, setGated] = useState(initialRequiresSignIn);
  const [chosen, setChosen] = useState<string[]>(initialStatuses);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  /*
   * The application form has no audience to set, so it is not offered one.
   *
   * Gating it would make applying impossible — the account it would demand is
   * the one applying creates — and the API and a check constraint both refuse
   * it. This is the third refusal and the only one that is a sentence rather
   * than an error, which is why it is worth having.
   */
  if (kind === "application") {
    return (
      <section className={styles.history}>
        <h2>Who can answer</h2>
        <p className="meta">
          Anybody with the link. The application form cannot require sign-in —
          applying is how somebody gets an account.
        </p>
      </section>
    );
  }

  function toggle(status: string) {
    setSaved(false);
    setChosen((current) =>
      current.includes(status)
        ? current.filter((s) => s !== status)
        : [...current, status],
    );
  }

  async function save() {
    setSaving(true);
    setNotice(null);

    const result = await saveAudience(formId, gated, gated ? chosen : []);

    setSaving(false);
    setNotice(result.ok ? null : (result.error ?? "That did not work."));
    setSaved(result.ok);
  }

  return (
    <section className={styles.history}>
      <h2>Who can answer</h2>

      <label>
        <input
          type="checkbox"
          checked={gated}
          disabled={!canManage || saving}
          onChange={(e) => {
            setGated(e.target.checked);
            setSaved(false);
          }}
        />{" "}
        Require sign-in
      </label>

      <p className="meta">
        A form that requires sign-in emails a link, fills in what we already
        hold, and files the answer against the person rather than an address
        they type.
      </p>

      {gated ? (
        <>
          <p className="meta">Applicants in these statuses can open it:</p>

          {/* Every status at once rather than behind a menu. There are eleven,
              they fit, and the question being answered is "which of these" —
              which is a list to look down, not a value to pick. */}
          <div role="group" aria-label="Statuses that can open this form">
            {statuses.map((status) => (
              <label key={status}>
                <input
                  type="checkbox"
                  checked={chosen.includes(status)}
                  disabled={!canManage || saving}
                  onChange={() => toggle(status)}
                />{" "}
                {status.replace(/_/g, " ")}
              </label>
            ))}
          </div>

          {/* Said here rather than only when the save is refused. A gate with
              nobody behind it is a form nobody can open, and finding that out
              from an error after pressing Save is finding it out later than
              necessary. */}
          {chosen.length === 0 ? (
            <p className="meta">Choose at least one, or nobody can open it.</p>
          ) : null}
        </>
      ) : null}

      {canManage ? (
        <button
          type="button"
          className="button"
          disabled={saving || (gated && chosen.length === 0)}
          onClick={() => void save()}
        >
          {saving ? "Saving…" : saved ? "Saved" : "Save"}
        </button>
      ) : null}

      {notice ? <p className="error">{notice}</p> : null}
    </section>
  );
}
