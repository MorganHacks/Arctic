import { redirect } from "next/navigation";
import { apiFetch, currentPerson, currentPermissions, type Listed } from "@/lib/api";
import { Shell } from "../shell";
import { AddOrganizer } from "./add-organizer";
import { PeopleTable } from "./people-table";

export default async function People() {
  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const response = await apiFetch("/admin/people");

  // 403 is not an error to recover from, it is the answer. The gate is doing
  // its job, and saying which permission is missing is what makes it possible
  // to ask for the right thing rather than "it doesn't work".
  if (response.status === 403) {
    return (
      <Shell personId={person.personId}>
        <h1>People</h1>
        <div className="empty">
          You do not have <code>people.view</code>. Ask an admin.
        </div>
      </Shell>
    );
  }

  if (!response.ok) {
    return (
      <Shell personId={person.personId}>
        <h1>People</h1>
        <div className="empty">People could not be loaded.</div>
      </Shell>
    );
  }

  const { people } = (await response.json()) as { people: Listed[] };

  // Cosmetic. The API refuses the write whether or not this form rendered, so
  // hiding it is a courtesy to someone who cannot use it rather than a control.
  const mine = await currentPermissions(person.personId);

  return (
    <Shell personId={person.personId}>
      <h1>People</h1>
      <p className="lede">
        Everyone with an account. Organizers sign in with Google; hackers get a
        link by email.
      </p>

      {mine.has("people.manage_teams") ? <AddOrganizer /> : null}

      {people.length === 0 ? (
        <div className="empty">Nobody yet.</div>
      ) : (
        <PeopleTable people={people} />
      )}
    </Shell>
  );
}
