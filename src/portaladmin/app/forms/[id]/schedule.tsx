"use client";

import { useState } from "react";
import { scheduleForm } from "../actions";
import styles from "./builder.module.css";
import { fromLocalInput, readable, toLocalInput } from "./when";

/**
 * When the form stops accepting answers.
 *
 * Beside the audience rather than under it, because they are the same kind of
 * decision: who may answer, and until when. Neither is a question on the form,
 * and both are usually settled once and then left alone.
 *
 * The whole difficulty here is that `closesAt` is an instant and nobody thinks
 * in instants. Somebody typing "January 15th at 11:59pm" means an evening in
 * the event's city, and the same evening written in UTC is the sixteenth at
 * four in the morning — a different calendar day, on the one field where the
 * calendar day is the entire point. So the input is read and written in the
 * event's zone, and every date this panel shows carries the zone abbreviation
 * so the reader can see which one they got. See ./when.ts for why that zone is
 * fixed rather than the browser's.
 *
 * Not part of the autosave, for the reason the audience is not: a deadline
 * half-typed is a deadline in the year 202, and writing that to a live form as
 * somebody types would close it.
 */
export function Schedule({
  formId,
  closesAt,
  canManage,
  onSaved,
}: {
  formId: string;

  /** The stored instant, or null for a form with no deadline. */
  closesAt: string | null;
  canManage: boolean;

  /** Pulls the header's pill back down, which is where the state is read. */
  onSaved: () => void;
}) {
  // The wall-clock form of the stored instant, which is what the input wants.
  // Seeded from the server and then owned here, because a field somebody is
  // typing into cannot be re-seeded underneath them on every refresh.
  const [value, setValue] = useState(() =>
    closesAt === null ? "" : toLocalInput(closesAt),
  );
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  // What the typed value actually means, resolved through the event's zone.
  // Echoed back below the field, which is the entire defence against somebody
  // setting a deadline a day out and not finding out until it fires.
  const resolved = readable(fromLocalInput(value));

  async function write(next: string | null) {
    setSaving(true);
    setNotice(null);

    const result = await scheduleForm(formId, next);

    setSaving(false);
    setSaved(result.ok);

    if (!result.ok) {
      setNotice(result.error ?? "That did not work.");
      return;
    }

    onSaved();
  }

  return (
    <section className={`panel ${styles.settingsPanel}`}>
      <h2>Deadline</h2>

      <div className="field">
        <label htmlFor="closesAt">Closes</label>
        <input
          id="closesAt"
          type="datetime-local"
          value={value}
          disabled={!canManage || saving}
          onChange={(e) => {
            setValue(e.target.value);
            setSaved(false);
          }}
        />
        <p className="hint">Eastern time, the same zone applicants see.</p>
      </div>

      {/* The typed time read back as an instant, zone named. A field that only
          ever shows what was typed into it cannot tell somebody they typed the
          wrong day, and this is the field where that mistake is expensive. */}
      {resolved ? (
        <p className="meta">Closes {resolved}.</p>
      ) : (
        <p className="meta">
          No deadline. The form stays open until somebody unpublishes it.
        </p>
      )}

      <p className="meta">
        After it closes the link still opens and says the deadline has passed.
        Answers already given are kept.
      </p>

      {canManage ? (
        <div className={styles.settingsActions}>
          <button
            type="button"
            className="button"
            disabled={saving || value === ""}
            onClick={() => void write(fromLocalInput(value))}
          >
            {saving ? "Saving…" : saved ? "Saved" : "Save"}
          </button>

          {/* Only when there is one to remove. A form with no deadline showing
              a button that removes its deadline is a button that does nothing,
              and a button that does nothing is one somebody presses to find
              out. */}
          {closesAt === null && value === "" ? null : (
            <button
              type="button"
              className="button"
              disabled={saving}
              onClick={() => {
                setValue("");
                void write(null);
              }}
            >
              Remove deadline
            </button>
          )}
        </div>
      ) : null}

      {notice ? <p className="error">{notice}</p> : null}
    </section>
  );
}
