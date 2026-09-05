/*
 * The event's zone, and the one way to render a date in it.
 *
 * Every date the system stores is an instant. Nobody thinks in instants:
 * somebody setting "confirm by January 15th at 11:59pm" means an evening in the
 * event's city, and that same instant written in UTC is the sixteenth at five in
 * the morning. On a deadline, that is a day out for exactly the person the date
 * was for — and the day is the part they act on.
 *
 * The zone is the event's rather than the reader's. Three reasons, in the order
 * they cost something when ignored:
 *
 *   1. The deadline on the flyer, the deadline in the console, and the deadline
 *      in the portal have to be the same sentence. An applicant on Pacific time
 *      reading their own browser's zone would be told a different evening from
 *      the one the team set and the one the email said.
 *   2. A fixed zone formats identically on the server and in the browser, so a
 *      date rendered in both cannot flicker between them on hydration.
 *   3. Support answers questions in this zone. "It says 11:59" has one meaning
 *      when everybody's screen agrees and none when it does not.
 *
 * The project has been caught by the offset before: the 2026 deadline was
 * written up as 11:59 PM EST in a month that was actually on EDT. Everything
 * here goes through Intl with the zone named, which gets the standard/daylight
 * switch right by itself, and every date it renders carries the abbreviation so
 * the reader can see which of the two they got.
 *
 * In libs/ui rather than inside an app because two of them need it. There were
 * three copies of this constant before it moved here -- the form builder's
 * deadline, the events screens and the public form -- which is three chances for
 * the console to announce a time the public form shows differently, with nothing
 * failing to say so. This is the reading side of the convention; the console's date *inputs* still parse
 * wall-clock text in app/forms/[id]/when.ts and components/events/zone.ts, and
 * those two remain the writing side. If any of the three ever disagree about
 * ZONE, this file is the one that is right.
 */

/** The zone every date said to a person is said in. */
export const ZONE = "America/New_York";

/**
 * A deadline somebody can act on, with the zone said out loud.
 *
 * The abbreviation is not decoration. Half the confusion these dates cause is
 * somebody in another state reading a time and assuming it is theirs, and the
 * three characters that prevent it cost nothing.
 */
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
 * "January 15, 2027 at 11:59 PM EST", or null when there is nothing to say.
 *
 * Null rather than a throw for anything unparseable, and null in and null out.
 * A date the browser cannot read is a line that should be absent from the page,
 * not a screen that fails to draw around it — and on this screen the rest of
 * the page is the part somebody needs.
 */
export function readableTime(iso: string | null): string | null {
  if (iso === null) {
    return null;
  }

  const instant = Date.parse(iso);
  return Number.isNaN(instant) ? null : READABLE.format(instant);
}
