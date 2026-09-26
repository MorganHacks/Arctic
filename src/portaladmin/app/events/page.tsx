import { redirect } from "next/navigation";
import { readPageData } from "@/lib/page-data";
import { EventsList } from "@/components/events/events-list";
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
  const { person, data: result } = await readPageData(() => listEvents());

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
      <EventsList events={result.events} now={Date.now()} canManage={person.permissions.has("events.manage")} />
    </Shell>
  );
}
