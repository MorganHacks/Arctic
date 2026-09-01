import { redirect } from "next/navigation";
import { apiFetch, currentPerson } from "@/lib/api";
import { Shell } from "../shell";

type Listed = {
  id: string;
  kind: string;
  email: string;
  revoked: boolean;
  teams: string[];
};

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

  return (
    <Shell personId={person.personId}>
      <h1>People</h1>
      <p className="lede">
        Everyone with an account. Organizers sign in with Google; hackers get a
        link by email.
      </p>

      {people.length === 0 ? (
        <div className="empty">Nobody yet.</div>
      ) : (
        <table>
          <thead>
            <tr>
              <th>Email</th>
              <th>Kind</th>
              <th>Teams</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {people.map((p) => (
              <tr key={p.id}>
                <td>{p.email}</td>
                <td>{p.kind}</td>
                <td>{p.teams.length > 0 ? p.teams.join(", ") : "—"}</td>
                <td>{p.revoked ? "Revoked" : "Active"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Shell>
  );
}
