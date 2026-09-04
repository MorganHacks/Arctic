"use client";

import { useState } from "react";
import { saveAudience } from "../actions";
import styles from "./builder.module.css";

/**
 * Who this form is for.
 *
 * At the top of the builder, above the questions, because that is the order the
 * decision is actually made in: an RSVP is for accepted applicants from the
 * moment it is a thought, and a feedback survey is for people who turned up.
 * Who a form is for shapes what it is reasonable to ask, so it belongs before
 * the asking rather than after it.
 *
 * It used to sit in the right-hand column underneath the preview, which was
 * wrong twice over. It was below the fold on a form of any length, so it was
 * missed; and it was directly under a panel drawn as the applicant's page,
 * which invited reading it as part of that preview rather than as a control
 * over who ever reaches the page.
 *
 * Not part of the autosave. Every other control on this screen writes as
 * somebody types, which is right for wording and wrong for a door: narrowing an
 * audience shuts a live form to people who were about to answer it, and a
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
   *
   * It says so rather than rendering nothing. An absent control on the panel
   * that every other form has at the top of its builder is a gap somebody
   * assumes is a bug, and they go looking for the setting on a screen that does
   * not have one.
   */
  if (kind === "application") {
    return (
      <section className={`panel ${styles.settingsPanel}`}>
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
    <section className={`panel ${styles.settingsPanel}`}>
      <h2>Who can answer</h2>

      <label className="check">
        <input
          type="checkbox"
          checked={gated}
          disabled={!canManage || saving}
          onChange={(e) => {
            setGated(e.target.checked);
            setSaved(false);
          }}
        />
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
              which is a list to look down, not a value to pick.

              Wrapping into columns rather than one tall column, which is what
              the sidebar forced. The panel is the full width of the page now,
              and eleven statuses stacked down it would push the questions off
              the screen for a control most forms set once. */}
          <div
            className={styles.statusGroup}
            role="group"
            aria-label="Statuses that can open this form"
          >
            {statuses.map((status) => (
              <label key={status} className="check">
                <input
                  type="checkbox"
                  checked={chosen.includes(status)}
                  disabled={!canManage || saving}
                  onChange={() => toggle(status)}
                />
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
        <div className={styles.settingsActions}>
          <button
            type="button"
            className="button"
            disabled={saving || (gated && chosen.length === 0)}
            onClick={() => void save()}
          >
            {saving ? "Saving…" : saved ? "Saved" : "Save"}
          </button>
        </div>
      ) : null}

      {notice ? <p className="error">{notice}</p> : null}
    </section>
  );
}
