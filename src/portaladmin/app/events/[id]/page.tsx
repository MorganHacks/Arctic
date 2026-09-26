import { ArrowLeft01Icon } from "@hugeicons/core-free-icons";
import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { Icon } from "@/components/ui/icon";
import { notFound, redirect } from "next/navigation";
import { currentPerson } from "@/lib/api";
import { EventDetails } from "@/components/events/event-details";
import { registrationState } from "@/components/events/types";
import { ZONE } from "@/components/events/zone";
import styles from "@/components/events/events.module.css";
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
        <BackToEvents />
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
        <BackToEvents />
        <h1>Event</h1>
        <div className="empty">Events could not be loaded.</div>
      </Shell>
    );
  }

  const event = result.events.find((candidate) => candidate.id === id);
  if (!event) {
    notFound();
  }

  const start = event.startsAt ? Date.parse(event.startsAt) : NaN;
  const end = event.endsAt ? Date.parse(event.endsAt) : NaN;
  const date = new Intl.DateTimeFormat("en-US", { timeZone: ZONE, month: "short", day: "numeric", year: "numeric" });
  const dateLabel = Number.isNaN(start) ? "Dates to be decided" : (
    !Number.isNaN(end) && end >= start ? date.formatRange(start, end) : date.format(start)
  ).replace(/\s+/gu, " ");

  return (
    <Shell personId={person.personId}>
      <EventDetails
        key={event.id}
        event={event}
        dateLabel={`${dateLabel}${!Number.isNaN(start) ? " · Eastern Time" : ""}`}
        registration={registrationState(event, Date.now())}
        announcements={notices.state === "ok" ? notices.announcements : []}
        canManage={person.permissions.has("events.manage")}
        canPost={notices.state === "ok"}
      />
    </Shell>
  );
}

function BackToEvents() {
  return <Link href="/events" className={styles.backLink}><Icon icon={ArrowLeft01Icon} size={18} />Events</Link>;
}
