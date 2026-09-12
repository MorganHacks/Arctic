import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { currentPerson } from "@/lib/api";
import { Announcements } from "@/components/events/announcements";
import { ScheduleForm } from "@/components/events/schedule";
import { registrationState, type Registration } from "@/components/events/types";
import { Shell } from "../../shell";
import { listEvents } from "../api";
import { listAnnouncements } from "../announcements";

/**
 * One event's dates and capacity.
 *
 * Separate from the list because they are settled one at a time over months,
 * usually by somebody who came here to change exactly one of them after a
 * meeting. The list is for checking; this is for editing.
 *
 * The name is editable here; the slug is not. The slug is what everything else
 * refers to this event by, and the create form is the one place it is ever
 * typed. The name is only what the console calls it, so a typo made in the
 * week an event was created is fixable in the month somebody notices.
 *
 * There is no endpoint for one event, so this reads the list and finds it.
 * That is one round trip either way, and an id the list does not carry is an
 * id that names nothing.
 */
export default async function Event({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const { id } = await params;

  // Both reads together rather than one after the other. Neither needs the
  // other's answer, and this screen is opened during an event by somebody who
  // is already late for the thing they are posting about.
  const [result, notices] = await Promise.all([
    listEvents(),
    listAnnouncements(id),
  ]);

  if (result.state === "signed-out") {
    redirect("/sign-in");
  }

  if (result.state === "forbidden") {
    return (
      <Shell personId={person.personId}>
        <Link href="/events" className="back">
          ← Events
        </Link>
        <h1>Event</h1>
        <p className="refusal">
          You do not have permission to see events. Ask an admin.
        </p>
      </Shell>
    );
  }

  if (result.state === "failed") {
    return (
      <Shell personId={person.personId}>
        <Link href="/events" className="back">
          ← Events
        </Link>
        <h1>Event</h1>
        <div className="empty">Events could not be loaded.</div>
      </Shell>
    );
  }

  const event = result.events.find((candidate) => candidate.id === id);
  if (!event) {
    notFound();
  }

  return (
    <Shell personId={person.personId}>
      <Link href="/events" className="back">
        ← Events
      </Link>

      <div className="page-head">
        <div>
          <h1>{event.name}</h1>
          <p className="lede mono">{event.slug}</p>
        </div>

        {/* Read the same way as the list, from the same two dates, so the two
            screens cannot disagree about whether applications are being
            taken. */}
        <div className="page-actions">
          <RegistrationPill state={registrationState(event, Date.now())} />
        </div>
      </div>

      <ScheduleForm event={event} />

      {/*
        A refusal on the list is not a reason to hide the panel. The permission
        is one somebody may plausibly lack while still administering the event,
        and a section that vanished would leave them wondering whether
        announcements exist at all rather than knowing they cannot post one.
      */}
      <Announcements
        eventId={event.id}
        announcements={notices.state === "ok" ? notices.announcements : []}
        canPost={notices.state === "ok"}
      />
    </Shell>
  );
}

/**
 * Whether applications are being taken, said in full.
 *
 * Longer than the list's version because there is no column heading here to
 * lean on, and a pill reading "Open" beside five date fields is a pill that
 * could mean any of them.
 */
function RegistrationPill({ state }: { state: Registration }) {
  if (state === "open") {
    return <span className="pill ok">Registration open</span>;
  }

  if (state === "upcoming") {
    return <span className="pill">Registration not open yet</span>;
  }

  if (state === "closed") {
    return <span className="pill lapsed">Registration closed</span>;
  }

  return <span className="pill lapsed">Registration not decided yet</span>;
}
