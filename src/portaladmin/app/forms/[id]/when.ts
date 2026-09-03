/*
 * Deadlines, in one zone, on purpose.
 *
 * A form's `closesAt` is an instant. Somebody typing "January 15th at 11:59pm"
 * is not thinking in instants — they mean an evening — and the two only line up
 * once a zone is named. Naming it is the whole of this file.
 *
 * The zone is the event's, not the browser's. Three reasons, in order of how
 * much they cost when ignored:
 *
 *   1. The applicant already sees the deadline in this zone. The public form
 *      renders it with `America/New_York` hard-coded, so an organizer whose
 *      laptop is on Pacific time and who types 11:59pm would be publishing a
 *      2:59am deadline to everybody reading the form. They would have no way
 *      to tell from this screen.
 *   2. The flyer says a wall-clock time in this zone. A console that agrees
 *      with the flyer is one where the two can be checked against each other.
 *   3. A fixed zone formats identically on the server and in the browser, so a
 *      deadline rendered in both places cannot flicker between them on
 *      hydration.
 *
 * The project has already been caught by the offset once: the 2026 deadline was
 * written up as 11:59 PM EST when September falls inside daylight saving and
 * the real offset was EDT. Everything here goes through Intl with the zone
 * named, which gets the standard/daylight switch right by itself, and every
 * date this file renders carries the abbreviation so the reader can see which
 * of the two they got.
 */

/** The zone every deadline in the console is written and read in. */
export const ZONE = "America/New_York";

/**
 * The wall-clock fields, so they can be compared with an instant.
 *
 * `hourCycle: "h23"` rather than `hour12: false`, which is the option that
 * actually stops midnight coming back as "24" — the two are not the same, and
 * the difference only shows up on one minute of the day.
 */
const FIELDS = new Intl.DateTimeFormat("en-US", {
  timeZone: ZONE,
  hourCycle: "h23",
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
});

/** A date somebody can read, with the zone said out loud. */
const READABLE = new Intl.DateTimeFormat("en-US", {
  timeZone: ZONE,
  year: "numeric",
  month: "long",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
  timeZoneName: "short",
});

/**
 * What the clock in the event's city read at this instant, as an epoch.
 *
 * Not a time — it is the wall-clock fields packed back into a number so they
 * can be subtracted from the instant they came from. That difference is the
 * zone's offset at that moment, daylight saving included.
 */
function wallClock(instant: number): number {
  const parts: Record<string, string> = {};
  for (const part of FIELDS.formatToParts(instant)) {
    parts[part.type] = part.value;
  }

  return Date.UTC(
    Number(parts.year),
    Number(parts.month) - 1,
    Number(parts.day),
    Number(parts.hour),
    Number(parts.minute),
    Number(parts.second),
  );
}

/**
 * An instant, as the value a `datetime-local` input wants.
 *
 * Empty for anything unparseable rather than throwing. A stored value the
 * browser cannot render is a field that should be blank and refillable, not a
 * screen that fails to draw.
 */
export function toLocalInput(iso: string): string {
  const instant = Date.parse(iso);
  if (Number.isNaN(instant)) {
    return "";
  }

  // Already the wall-clock fields as an epoch, so the UTC rendering of it is
  // those fields verbatim — which is exactly the input's format.
  return new Date(wallClock(instant)).toISOString().slice(0, 16);
}

/**
 * What somebody typed, as the instant it means.
 *
 * Solved rather than calculated, because the offset depends on the answer: the
 * zone's offset at the instant we are trying to find is what tells us which
 * instant it is. Two passes settle it — the first gets within an hour, the
 * second lands on it — and they only ever disagree across a daylight-saving
 * boundary, which is the case worth being right about.
 */
export function fromLocalInput(local: string): string | null {
  if (local === "") {
    return null;
  }

  // The input omits seconds. Read as if the fields were UTC, which is not the
  // answer but is the number the fixpoint below starts from.
  const naive = Date.parse(`${local.length === 16 ? `${local}:00` : local}Z`);
  if (Number.isNaN(naive)) {
    return null;
  }

  let instant = naive - (wallClock(naive) - naive);
  instant = naive - (wallClock(instant) - instant);

  return new Date(instant).toISOString();
}

/** "January 15, 2027 at 11:59 PM EST", or null if there is nothing to say. */
export function readable(iso: string | null): string | null {
  if (iso === null) {
    return null;
  }

  const instant = Date.parse(iso);
  return Number.isNaN(instant) ? null : READABLE.format(instant);
}
