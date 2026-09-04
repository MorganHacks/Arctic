"use client";

import { useState } from "react";
import { saveSchedule, type Schedule } from "@/app/events/actions";
import styles from "./events.module.css";
import type { EventRow } from "./types";
import { fromLocalInput, readable, toLocalInput } from "./zone";

/**
 * The dates on an event, and how many people it is for.
 *
 * All of it optional, all of it clearable, and empty is the ordinary state
 * rather than a fault. An event is created in the week somebody decides to run
 * one and dated over the months after; a screen that treated a blank field as
 * a mistake would be complaining about the normal condition of its own
 * subject for most of the year.
 *
 * The difficulty here is that every one of these is an instant and nobody
 * thinks in instants. "Registration opens January 15th at midnight" means a
 * midnight in the event's city, and the same midnight written in UTC is a
 * different calendar day. So the fields are read and written in the event's
 * zone, and every date is echoed back under its field with the zone named.
 * See ./zone.ts for why that zone is fixed rather than the reader's.
 */
const DATES = [
  { key: "registrationOpensAt", label: "Registration opens" },
  { key: "registrationClosesAt", label: "Registration closes" },
  { key: "startsAt", label: "Event starts" },
  { key: "endsAt", label: "Event ends" },
  { key: "decisionsAnnouncedAt", label: "Decisions announced" },
] as const;

type DateKey = (typeof DATES)[number]["key"];

export function ScheduleForm({ event }: { event: EventRow }) {
  // The wall-clock form of each stored instant, which is what the input wants.
  // Seeded once from the server and owned here after: a field somebody is
  // typing into cannot be re-seeded underneath them on every refresh.
  const [dates, setDates] = useState<Record<DateKey, string>>(() => ({
    registrationOpensAt: toLocalInput(event.registrationOpensAt),
    registrationClosesAt: toLocalInput(event.registrationClosesAt),
    startsAt: toLocalInput(event.startsAt),
    endsAt: toLocalInput(event.endsAt),
    decisionsAnnouncedAt: toLocalInput(event.decisionsAnnouncedAt),
  }));

  const [capacity, setCapacity] = useState(
    event.capacity === null ? "" : String(event.capacity),
  );

  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  function edit(key: DateKey, value: string) {
    setDates((current) => ({ ...current, [key]: value }));
    setSaved(false);
    setNotice(null);
  }

  async function save() {
    setSaving(true);
    setNotice(null);

    // Every field, every time, nulls included. Sending only what changed would
    // make clearing a date indistinguishable from leaving it alone, and going
    // back to undecided is a thing that genuinely happens.
    const schedule: Schedule = {
      registrationOpensAt: fromLocalInput(dates.registrationOpensAt),
      registrationClosesAt: fromLocalInput(dates.registrationClosesAt),
      startsAt: fromLocalInput(dates.startsAt),
      endsAt: fromLocalInput(dates.endsAt),
      decisionsAnnouncedAt: fromLocalInput(dates.decisionsAnnouncedAt),
      capacity: readCapacity(capacity),
    };

    const result = await saveSchedule(event.id, schedule);

    setSaving(false);
    setSaved(result.ok);

    if (!result.ok) {
      setNotice(result.error ?? "That did not work.");
    }
  }

  return (
    <>
      <section className="panel">
        <div className="panel-head">
          <h2>Dates</h2>
        </div>

        <p className="hint">
          Times are Eastern, the same zone applicants see. Any date can be left
          empty until it is decided.
        </p>

        <div className={styles.grid}>
          {DATES.map((field) => (
            <DateField
              key={field.key}
              id={field.key}
              label={field.label}
              value={dates[field.key]}
              disabled={saving}
              onChange={(value) => edit(field.key, value)}
            />
          ))}
        </div>
      </section>

      <section className="panel">
        <div className="panel-head">
          <h2>Capacity</h2>
        </div>

        <div className="field">
          <div className={styles.fieldHead}>
            <label htmlFor="capacity">Places</label>
            <button
              type="button"
              className={styles.clear}
              disabled={saving || capacity === ""}
              onClick={() => {
                setCapacity("");
                setSaved(false);
                setNotice(null);
              }}
            >
              Clear
            </button>
          </div>

          <input
            id="capacity"
            type="number"
            min={0}
            step={1}
            inputMode="numeric"
            className={styles.capacityInput}
            value={capacity}
            disabled={saving}
            onChange={(e) => {
              setCapacity(e.target.value);
              setSaved(false);
              setNotice(null);
            }}
          />

          <p className={styles.echo}>
            {capacity.trim() === "" ? "Not decided yet." : null}
          </p>
        </div>
      </section>

      <div className={styles.actions}>
        <button
          type="button"
          className="button primary"
          disabled={saving}
          onClick={() => void save()}
        >
          {saving ? "Saving…" : "Save"}
        </button>

        {saved ? <span className={styles.saved}>Saved</span> : null}
      </div>

      {notice ? <p className="error">{notice}</p> : null}
    </>
  );
}

/**
 * One date, with what it means printed under it.
 *
 * The echo is the whole reason this is a component rather than five inputs. A
 * field that only ever shows the characters typed into it cannot tell somebody
 * they have set registration to open on the wrong day, and the wrong day is
 * the mistake this screen exists to prevent.
 *
 * Empty says so in words rather than showing a blank line, because a blank
 * under a blank field looks like the screen failed to work something out.
 */
function DateField({
  id,
  label,
  value,
  disabled,
  onChange,
}: {
  id: string;
  label: string;
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  const resolved = readable(fromLocalInput(value));

  return (
    <div className="field">
      <div className={styles.fieldHead}>
        <label htmlFor={id}>{label}</label>
        {/* Only offered when there is something to remove. A button that does
            nothing is a button somebody presses to find out. */}
        <button
          type="button"
          className={styles.clear}
          disabled={disabled || value === ""}
          onClick={() => onChange("")}
        >
          Clear
        </button>
      </div>

      <input
        id={id}
        type="datetime-local"
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
      />

      <p className={styles.echo}>{resolved ?? "Not decided yet."}</p>
    </div>
  );
}

/** What was typed in the capacity box, as a number or as nothing. */
function readCapacity(value: string): number | null {
  const trimmed = value.trim();
  if (trimmed === "") {
    return null;
  }

  const places = Number(trimmed);
  return Number.isFinite(places) && places >= 0 ? Math.trunc(places) : null;
}
