"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useState } from "react";
import { ArrowDown01Icon, Calendar03Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { DateTimePicker } from "@/components/ui/date-time-picker";
import { compact } from "@/components/events/zone";
import { scheduleForm } from "../actions";
import styles from "./builder.module.css";
import { fromLocalInput, toLocalInput } from "./when";

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
    <details className={styles.settingsPanel} data-setting="deadline">
      <summary className={styles.settingsSummary}>
        <span className={styles.settingsIcon}><Icon icon={Calendar03Icon} size={19} /></span>
        <span className={styles.settingsCopy}><span>Deadline</span><strong>{compact(fromLocalInput(value)) || "No deadline"}</strong></span>
        <Icon icon={ArrowDown01Icon} size={16} />
      </summary>
      <div className={styles.settingsBody}>

        <div className={styles.deadlineField}>
          <div className={styles.deadlineLabel}>
            <label htmlFor="closesAt">Closing date & time</label>
            <span id="deadline-zone">Eastern time</span>
          </div>
          <DateTimePicker
            id="closesAt"
            label="Closing date and time"
            describedBy="deadline-zone"
            value={value}
            disabled={!canManage || saving}
            onChange={(next) => {
              setValue(next);
              setSaved(false);
            }}
          />
        </div>
        <p className={styles.settingsHint}>
          {value ? "New responses stop at the deadline. Existing answers are kept."
            : "Accept responses until you unpublish or set a deadline."}
        </p>

        {canManage ? (
          <div className={styles.settingsActions}>
            <button
              type="button"
              className="button"
              disabled={saving || value === ""}
              onClick={() => void write(fromLocalInput(value))}
            >
              {saving ? "Saving…" : saved ? "Saved" : "Save deadline"}
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

        <ErrorToast message={notice} />
      </div>
    </details>
  );
}
