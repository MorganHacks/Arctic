/*
 * An event's dates, in one zone, on purpose.
 *
 * Every date on an event is an instant. Nobody thinks in instants: somebody
 * typing "registration opens January 15th at midnight" means a midnight in the
 * event's city, and that same midnight written in UTC is the fifteenth at five
 * in the morning — or the sixteenth, depending on the month. On the fields
 * where the calendar day is the entire point, that is a day out for exactly
 * the people the date was for.
 *
 * So the zone is fixed, and it is the event's rather than the reader's:
 *
 *   1. The public form already renders its deadline in the event's zone, and
 *      the forms builder already reads and writes deadlines in it. An events
 *      screen on the browser's zone would mean an organizer on Pacific time
 *      typing a date here and reading a different one on the next screen.
 *   2. A flyer says a wall-clock time in this zone. A console that agrees with
 *      the flyer is one where the two can be checked against each other.
 *   3. A fixed zone formats identically on the server and in the browser, so a
 *      date rendered in both cannot flicker between them on hydration.
 *
 * The project has been caught by the offset before: a deadline written up as
 * 11:59 PM EST in a month that was actually on EDT. Everything here goes
 * through Intl with the zone named, which gets the standard/daylight switch
 * right by itself, and every date rendered carries the abbreviation so the
 * reader can see which of the two they got.
 *
 * This is deliberately the same convention as app/forms/[id]/when.ts, restated
 * here rather than imported, because these screens do not own that file and a
 * date convention shared by copy is safer than one shared by a reach across
 * two screens' boundaries. If the two ever disagree, this file is the one that
 * is wrong.
 */

/** The zone every date in the console is written and read in. */
// One definition, in libs/ui. This file keeps the formatting helpers;
// the zone itself is shared so three screens cannot drift apart.
import { ZONE } from "../../../../libs/ui/zone";

export { ZONE };

/**
 * The wall-clock fields, so they can be compared with an instant.
 *
 * `hourCycle: "h23"` rather than `hour12: false`, which is the option that
 * actually stops midnight coming back as "24" — the two are not the same, and
 * the difference only shows up on one minute of the day. Midnight is the most
 * likely thing anybody types into a registration field.
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

/** The same thing, short enough to sit in a table cell. */
const COMPACT = new Intl.DateTimeFormat("en-US", {
  timeZone: ZONE,
  year: "numeric",
  month: "short",
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
export function toLocalInput(iso: string | null): string {
  if (iso === null) {
    return "";
  }

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
 *
 * An empty field is null, not an error. Every date on an event is allowed to
 * be undecided, and clearing one is how it goes back to being undecided.
 */
export function fromLocalInput(local: string): string | null {
  if (local.trim() === "") {
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

/** The same, for a table cell: "Jan 15, 2027, 11:59 PM EST". */
export function compact(iso: string | null): string | null {
  if (iso === null) {
    return null;
  }

  const instant = Date.parse(iso);
  return Number.isNaN(instant) ? null : COMPACT.format(instant);
}
