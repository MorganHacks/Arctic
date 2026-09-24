import { apiFetch, type Listed } from "@/lib/api";
import { readPageData } from "@/lib/page-data";
import { Shell } from "../shell";
import { AddOrganizer } from "./add-organizer";
import { PeopleTable } from "./people-table";
import styles from "./members.module.css";

export default async function People() {
  const { person, data: response } = await readPageData(() => apiFetch("/admin/people"));

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

  return (
    <Shell personId={person.personId}>
      <div className={styles.page}>
        <PeopleTable people={people} />

        {/* The API independently enforces this permission on every write. */}
        {person.permissions.has("people.manage_teams") ? <AddOrganizer /> : null}
      </div>
    </Shell>
  );
}
