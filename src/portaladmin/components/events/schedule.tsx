"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useState, type ReactNode } from "react";
import { Clock01Icon, Link04Icon } from "@hugeicons/core-free-icons";
import { DateTimePicker } from "@/components/ui/date-time-picker";
import { Icon } from "@/components/ui/icon";
import { saveEvent, type EventEdit } from "@/app/events/actions";
import styles from "./events.module.css";
import type { EventRow } from "./types";
import { fromLocalInput, readable, toLocalInput } from "./zone";

/**
 * An event's name, its dates, and how many people it is for.
 *
 * The dates and the capacity are optional and clearable, and empty is the
 * ordinary state rather than a fault. An event is created in the week somebody
 * decides to run one and dated over the months after; a screen that treated a
 * blank field as a mistake would be complaining about the normal condition of
 * its own subject for most of the year.
 *
 * The name is the exception: an event always has one, because creating it
 * required one. It is editable here because it is only a label — the slug
 * beside it is the identifier, is not editable anywhere, and is the reason
 * renaming is safe.
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

export function ScheduleForm({ event, canManage, active, renderHeader }: {
  event: EventRow;
  canManage: boolean;
  active: boolean;
  renderHeader: (state: { saving: boolean; saved: boolean; dirty: boolean; canSave: boolean; discard: () => void }) => ReactNode;
}) {
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

  // Owned here for the same reason the dates are: it is a field somebody is
  // typing into, and a re-render that re-seeded it from the server would take
  // the half-typed name away mid-word.
  const [name, setName] = useState(event.name);
  const [savedValues, setSavedValues] = useState(() => ({ name, dates, capacity }));

  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const disabled = saving || !canManage;
  const dirty = name !== savedValues.name || capacity !== savedValues.capacity || DATES.some(({ key }) => dates[key] !== savedValues.dates[key]);
  const canSave = !disabled && dirty && name.trim() !== "";

  function discard() {
    setName(savedValues.name);
    setDates(savedValues.dates);
    setCapacity(savedValues.capacity);
    touched();
  }

  function edit(key: DateKey, value: string) {
    setDates((current) => ({ ...current, [key]: value }));
    touched();
  }

  /* Every field clears the confirmation and the last refusal, because both are
     statements about the values as they were when Save was pressed. */
  function touched() {
    setSaved(false);
    setNotice(null);
  }

  async function save() {
    if (!canSave) return;
    setSaving(true);
    setNotice(null);

    // Every field, every time, nulls included. Sending only what changed would
    // make clearing a date indistinguishable from leaving it alone, and going
    // back to undecided is a thing that genuinely happens.
    const edit: EventEdit = {
      name: name.trim(),
      registrationOpensAt: fromLocalInput(dates.registrationOpensAt),
      registrationClosesAt: fromLocalInput(dates.registrationClosesAt),
      startsAt: fromLocalInput(dates.startsAt),
      endsAt: fromLocalInput(dates.endsAt),
      decisionsAnnouncedAt: fromLocalInput(dates.decisionsAnnouncedAt),
      capacity: readCapacity(capacity),
    };

    const result = await saveEvent(event.id, edit);

    setSaving(false);
    setSaved(result.ok);

    if (!result.ok) {
      setNotice(result.error ?? "That did not work.");
    } else {
      setName(name.trim());
      setSavedValues({ name: name.trim(), dates, capacity });
    }
  }

  return (
    <>
      {renderHeader({ saving, saved, dirty, canSave, discard })}
      <div className={styles.panel} role="tabpanel" id="event-panel-0" aria-labelledby="event-tab-0" hidden={!active} tabIndex={0}>
        <form id="event-settings" className={styles.settings} onSubmit={(event) => { event.preventDefault(); void save(); }}>
          <section className={styles.section} aria-labelledby="event-details-title">
            <div className={styles.sectionHeading}>
              <h2 id="event-details-title">Event details</h2>
              <p>Give your event a name and set its capacity.</p>
            </div>

            <div className={styles.generalFields}>
              <div className={styles.field}>
                <label htmlFor="name">Event name</label>
                <input
                  id="name"
                  value={name}
                  disabled={disabled}
                  required
                  autoComplete="off"
                  onChange={(e) => {
                    setName(e.target.value);
                    touched();
                  }}
                />
              </div>
              <div className={styles.field}>
                <div className={styles.fieldHead}>
                  <label htmlFor="capacity">Capacity</label>
                  {canManage ? <button type="button" className={styles.clear} disabled={saving || capacity === ""}
                    aria-label="Clear capacity" onClick={() => { setCapacity(""); touched(); }}>Clear</button> : null}
                </div>
                <div className={styles.capacityField}>
                  <input id="capacity" type="number" min={0} step={1} inputMode="numeric"
                    value={capacity} disabled={disabled} placeholder="Not set"
                    onChange={(event) => { setCapacity(event.target.value); touched(); }} />
                  <span>places</span>
                </div>
              </div>
              <p className={styles.slug}><span>Event slug</span><code><Icon icon={Link04Icon} size={15} strokeWidth={1.8} /><span>{event.slug}</span></code><span>Used in links and forms. Cannot be changed.</span></p>
            </div>
          </section>

          <section className={styles.section} aria-labelledby="event-schedule-title">
            <div className={styles.sectionHeading}>
              <div className={styles.sectionTitle}>
                <h2 id="event-schedule-title">Event schedule</h2>
                <span className={styles.timezone}><Icon icon={Clock01Icon} size={14} />Eastern Time</span>
              </div>
              <p>Set the start and end of your event. Dates can be left open.</p>
            </div>

            <div className={styles.grid}>
              {DATES.slice(2, 4).map((field) => (
                <DateField
                  key={field.key}
                  id={field.key}
                  label={field.label}
                  value={dates[field.key]}
                  disabled={disabled}
                  canManage={canManage}
                  onChange={(value) => edit(field.key, value)}
                />
              ))}
            </div>
          </section>

          <section className={styles.section} aria-labelledby="event-registration-title">
            <div className={styles.sectionHeading}>
              <h2 id="event-registration-title">Registration</h2>
              <p>Choose when applications open, close and receive a decision.</p>
            </div>
            <div className={`${styles.grid} ${styles.registrationFields}`}>
              {DATES.filter((field) => field.key !== "startsAt" && field.key !== "endsAt").map((field) => (
                <DateField key={field.key} id={field.key} label={field.label}
                  value={dates[field.key]} disabled={disabled} canManage={canManage}
                  onChange={(value) => edit(field.key, value)} />
              ))}
            </div>
          </section>

          {notice ? <ErrorToast message={notice} /> : null}

          {!canManage ? <p className={styles.saveHint}>You can view these settings. An event admin can make changes.</p> : null}
        </form>
      </div>
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
  canManage,
  onChange,
}: {
  id: string;
  label: string;
  value: string;
  disabled: boolean;
  canManage: boolean;
  onChange: (value: string) => void;
}) {
  const resolved = readable(fromLocalInput(value));

  return (
    <div className={styles.field}>
      <div className={styles.fieldHead}>
        <label htmlFor={id}>{label}</label>
        {/* Only offered when there is something to remove. A button that does
            nothing is a button somebody presses to find out. */}
        {canManage ? <button
          type="button"
          className={styles.clear}
          disabled={disabled || value === ""}
          aria-label={`Clear ${label.toLowerCase()}`}
          onClick={() => onChange("")}
        >
          Clear
        </button> : null}
      </div>

      <DateTimePicker id={id} label={label} value={value} disabled={disabled}
        describedBy={`${id}-description`} onChange={onChange} />

      <p id={`${id}-description`} className={styles.echo}>{resolved ?? "Not decided yet."}</p>
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
