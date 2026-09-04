/**
 * One event, with everything these screens can set on it.
 *
 * Every date and the capacity are nullable, and null is the normal state for
 * most of them for most of the year. An event is created from a slug and a
 * name because that is all anybody knows in the week it is created; the dates
 * arrive one at a time over the months after, and a screen that treats a
 * missing one as a fault is a screen that is wrong about its own subject.
 */
export type EventRow = {
  id: string;
  slug: string;
  name: string;
  startsAt: string | null;
  endsAt: string | null;
  registrationOpensAt: string | null;
  registrationClosesAt: string | null;
  decisionsAnnouncedAt: string | null;
  capacity: number | null;
};

/**
 * Whether applications are being taken right now, and if not, why not.
 *
 * Four answers rather than two. "Not decided yet" is not "closed": one is a
 * date nobody has set and the other is a date that has passed, and an
 * organizer looking at this list in October needs to be able to tell them
 * apart at a glance.
 *
 * Registration counts as open only when the opening date is set and has
 * passed. A closing date on its own does not open anything — inferring that it
 * did would mean the console announcing that applications are being taken on
 * the strength of a field nobody filled in.
 */
export type Registration = "open" | "closed" | "upcoming" | "undecided";

export function registrationState(event: EventRow, now: number): Registration {
  const opens = parse(event.registrationOpensAt);
  const closes = parse(event.registrationClosesAt);

  if (closes !== null && closes <= now) {
    return "closed";
  }

  if (opens === null) {
    return "undecided";
  }

  return opens <= now ? "open" : "upcoming";
}

function parse(iso: string | null): number | null {
  if (iso === null) {
    return null;
  }

  const instant = Date.parse(iso);
  return Number.isNaN(instant) ? null : instant;
}
