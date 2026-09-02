import Link from "next/link";
import { redirect } from "next/navigation";
import {
  apiFetch,
  currentPerson,
  type FormsView,
} from "@/lib/api";
import { Shell } from "../shell";
import { NewForm } from "./new-form";

/**
 * The forms on one event.
 *
 * Two things are on every row because they are the two questions somebody
 * opens this screen to answer: what is the link, and is it live. A form that
 * has never been published is not broken — somebody is still writing it — so
 * that reads as a state rather than a warning.
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
  // hiding it is a courtesy to somebody who cannot use it rather than a
  // control over anything.
  const mine = person.permissions;

  if (!chosen) {
    return (
      <Shell personId={person.personId}>
        <h1>Forms</h1>
        <div className="empty">
          There is no event yet. A form belongs to one, so there is nowhere to
          put a question until somebody creates it.
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
        <div className="empty">
          No forms on {chosen.name} yet. The application form starts with MLH&rsquo;s
          questions already on it.
        </div>
      ) : (
        <table>
          <thead>
            <tr>
              <th>Form</th>
              <th>Kind</th>
              <th>Code</th>
              <th>Status</th>
              <th>Questions</th>
            </tr>
          </thead>
          <tbody>
            {forms.map((form) => (
              <tr key={form.id}>
                <td>
                  <Link href={`/forms/${form.id}`}>{form.name}</Link>
                </td>
                <td>{form.kind}</td>
                <td>
                  {/* The thing people read aloud at a club meeting and write
                      on a whiteboard, so it is set in mono and left alone. */}
                  <code>{form.code}</code>
                </td>
                <td>
                  {form.published ? (
                    <span className="pill active">
                      Live · v{form.publishedVersion}
                    </span>
                  ) : (
                    <span className="pill lapsed">Draft only</span>
                  )}
                </td>
                <td>{form.questions ?? "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Shell>
  );
}
