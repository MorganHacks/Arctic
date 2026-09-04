import { redirect } from "next/navigation";
import { currentPerson } from "@/lib/api";
import { EventsTable } from "@/components/events/events-table";
import { NewEvent } from "@/components/events/new-event";
import { NoEvents } from "@/components/events/no-events";
import { Shell } from "../shell";
import { listEvents } from "./api";

/**
 * The events everything else belongs to.
 *
 * First in the nav because it is first in the work. A form belongs to an
 * event, an applicant belongs to an event, and a mail segment is a question
 * asked about one, so in an environment where no event exists every other
 * screen in the console is empty and none of them can say why.
 *
 * Until now the only way to make one was to write the INSERT by hand, which is
 * exactly why staging has never had an event and somebody's laptop does.
 */
export default async function Events() {
  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const result = await listEvents();

  if (result.state === "signed-out") {
    redirect("/sign-in");
  }

  if (result.state === "forbidden") {
    return (
      <Shell personId={person.personId}>
        <h1>Events</h1>
        <p className="refusal">
          You do not have permission to see events. Ask an admin.
        </p>
      </Shell>
    );
  }

  if (result.state === "failed") {
    return (
      <Shell personId={person.personId}>
        <h1>Events</h1>
        <div className="empty">Events could not be loaded.</div>
      </Shell>
    );
  }

  return (
    <Shell personId={person.personId}>
      <div className="page-head">
        <div>
          <h1>Events</h1>
          <p className="lede">
            Everything in the console belongs to an event. Forms, applicants and
            mail are each scoped to one.
          </p>
        </div>
      </div>

      {/* Shown to everybody. Hiding it would be a courtesy to somebody who
          cannot use it, not a control over anything: the API refuses the write
          whether or not this panel rendered, and that refusal is the boundary. */}
      <NewEvent />

      {result.events.length === 0 ? (
        <NoEvents />
      ) : (
        /*
         * The clock is read once, here, and handed down.
         *
         * An event whose registration closes while this page is being rendered
         * would otherwise be able to come out Open in one row's reckoning and
         * Closed in the next, which is a bug nobody would ever reproduce.
         */
        <div className="table-wrap">
          <EventsTable events={result.events} now={Date.now()} />
        </div>
      )}
    </Shell>
  );
}
