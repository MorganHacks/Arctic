import Link from "next/link";
import { redirect } from "next/navigation";
import { apiFetch, currentPerson, type FormsView } from "@/lib/api";
import { FormsTable } from "@/components/formslist/forms-table";
import { NoForms } from "@/components/formslist/no-forms";
import { Shell } from "../shell";
import { NewForm } from "./new-form";

/**
 * The forms on one event.
 *
 * The screen answers three questions and nothing else: what is the link, is it
 * live, and where do I go to edit it. Anything a form has that is not one of
 * those belongs in the builder, which is one press away on every row.
 */
export default async function Forms({
  searchParams,
}: {
  searchParams: Promise<{ event?: string }>;
}) {
  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const { event } = await searchParams;
  const response = await apiFetch(
    `/admin/forms${event ? `?eventId=${encodeURIComponent(event)}` : ""}`,
  );

  if (response.status === 403) {
    return (
      <Shell personId={person.personId}>
        <h1>Forms</h1>
        <div className="empty">
          You do not have <code>applications.view</code>. Ask an admin.
        </div>
      </Shell>
    );
  }

  if (!response.ok) {
    return (
      <Shell personId={person.personId}>
        <h1>Forms</h1>
        <div className="empty">Forms could not be loaded.</div>
      </Shell>
    );
  }

  const { events, chosen, forms } = (await response.json()) as FormsView;

  // Cosmetic. The API refuses the write whether or not this panel rendered, so
  // hiding it is a courtesy to somebody who cannot use it rather than a control
  // over anything.
  const mine = person.permissions;

  if (!chosen) {
    return (
      <Shell personId={person.personId}>
        <h1>Forms</h1>
        <div className="empty">
          There is no event yet. A form belongs to one, so create an event
          under Events first.
        </div>
      </Shell>
    );
  }

  return (
    <Shell personId={person.personId}>
      <h1>Forms</h1>
      <p className="lede">
        Everything an applicant, mentor or volunteer is asked to fill in. Each
        one has a code that goes on the flyer and outlives every edit.
      </p>

      {/* Only when there is a choice to make. One event is the normal case and
          a dropdown with one option in it is furniture. */}
      {events.length > 1 ? (
        <div className="filters">
          <div>
            <label htmlFor="event">Event</label>
            <div className="tabs">
              {events.map((option) => (
                <Link
                  key={option.id}
                  href={`/forms?event=${option.id}`}
                  className={option.id === chosen.id ? "tab on" : "tab"}
                >
                  {option.name}
                </Link>
              ))}
            </div>
          </div>
        </div>
      ) : null}

      {mine.has("forms.manage") ? <NewForm eventId={chosen.id} /> : null}

      {forms.length === 0 ? (
        <NoForms event={chosen.name} />
      ) : (
        /*
         * The clock is read once, here, and handed down.
         *
         * A form that closes while this page is being rendered would otherwise
         * be able to come out Live in one row's reckoning and Closed in the
         * next, which is a bug nobody would ever reproduce.
         */
        <FormsTable forms={forms} now={Date.now()} />
      )}
    </Shell>
  );
}
