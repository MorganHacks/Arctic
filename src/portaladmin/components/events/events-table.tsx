import Link from "next/link";
import styles from "./events.module.css";
import { registrationState, type EventRow } from "./types";
import { compact } from "./zone";

/**
 * Every event, newest first, as the API returns them.
 *
 * The list answers two questions and nothing else: are we taking applications
 * right now, and what is settled about the dates. Everything else about an
 * event lives on the screen one press away, because everything else about an
 * event is something being edited rather than something being checked.
 *
 * Dates are shown in the event's zone with the abbreviation on every one of
 * them. A list of instants rendered in whatever zone the reader's laptop is
 * set to is a list that says something different to two people looking at it
 * together, on the fields where the calendar day is the whole point.
 */
export function EventsTable({ events, now }: { events: EventRow[]; now: number }) {
  return (
    <table>
      <thead>
        <tr>
          <th>Event</th>
          <th>Registration</th>
          <th>Dates</th>
          <th>Capacity</th>
        </tr>
      </thead>
      <tbody>
        {events.map((event) => (
          <tr key={event.id}>
            <td className={styles.cell}>
              <Link href={`/events/${event.id}`} className={styles.name}>
                {event.name}
              </Link>
              <div>
                <span className={styles.slug}>{event.slug}</span>
              </div>
            </td>

            <td className={styles.cell}>
              <Registration event={event} now={now} />
            </td>

            <td className={styles.cell}>
              <Dates event={event} />
            </td>

            <td className={styles.cell}>
              {event.capacity === null ? (
                <NotDecided />
              ) : (
                <span className="numeric">{event.capacity}</span>
              )}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/**
 * Whether applications are being taken, right now, as this page was rendered.
 *
 * Four states, not two. A date nobody has set and a date that has passed are
 * different facts about an event, and telling them apart is most of the value
 * of this column in the nine months of the year when registration is not open.
 * Only the open state is coloured, because it is the only one of the four that
 * means somebody is out there filling in a form.
 */
function Registration({ event, now }: { event: EventRow; now: number }) {
  const state = registrationState(event, now);
  const opens = compact(event.registrationOpensAt);
  const closes = compact(event.registrationClosesAt);

  if (state === "open") {
    return (
      <>
        <span className="pill ok">Open</span>
        {closes === null ? null : <div className={styles.when}>Closes {closes}</div>}
      </>
    );
  }

  if (state === "closed") {
    return (
      <>
        <span className="pill lapsed">Closed</span>
        {closes === null ? null : <div className={styles.when}>Closed {closes}</div>}
      </>
    );
  }

  if (state === "upcoming") {
    return (
      <>
        <span className="pill">Not open yet</span>
        {opens === null ? null : <div className={styles.when}>Opens {opens}</div>}
      </>
    );
  }

  return <span className="pill lapsed">Not decided yet</span>;
}

/**
 * The three dates that are not registration, labelled.
 *
 * Labelled rather than columned because most of them are empty most of the
 * year: three blank columns read as a table that failed to load, where three
 * labelled blanks read as three decisions nobody has taken.
 */
function Dates({ event }: { event: EventRow }) {
  return (
    <dl className={styles.dateList}>
      <dt className={styles.dateTerm}>Starts</dt>
      <dd className={styles.dateValue}>
        <Instant iso={event.startsAt} />
      </dd>

      <dt className={styles.dateTerm}>Ends</dt>
      <dd className={styles.dateValue}>
        <Instant iso={event.endsAt} />
      </dd>

      <dt className={styles.dateTerm}>Decisions</dt>
      <dd className={styles.dateValue}>
        <Instant iso={event.decisionsAnnouncedAt} />
      </dd>
    </dl>
  );
}

function Instant({ iso }: { iso: string | null }) {
  const when = compact(iso);
  return when === null ? <NotDecided /> : <>{when}</>;
}

/**
 * The ordinary state of most of these fields.
 *
 * One phrase everywhere it happens, and the quietest ink on the screen. Said
 * eleven different ways it would read as eleven different problems; said in
 * red it would read as one.
 */
function NotDecided() {
  return <span className={styles.undecided}>Not decided yet</span>;
}
