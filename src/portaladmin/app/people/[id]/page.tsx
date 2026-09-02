import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import {
  apiFetch,
  currentPerson,
  currentPermissions,
  type Catalogue,
  type PersonDetail,
} from "@/lib/api";
import { Shell } from "../../shell";
import { Grants, Revoke, Teams, type GrantRow, type TeamRow } from "./controls";

/**
 * One person, and the controls for changing what they can do.
 *
 * Everything on this screen answers one question: why can this person do what
 * they can do. Teams, grants and the union of the two are shown together
 * because the answer is almost never in one of them alone — "they lost export
 * when their judge membership lapsed" is only readable if the lapsed
 * membership is still on the page.
 */
export default async function PersonPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;

  const viewer = await currentPerson();
  if (!viewer) {
    redirect("/sign-in");
  }

  const [personResponse, catalogueResponse] = await Promise.all([
    apiFetch(`/admin/people/${id}`),
    apiFetch("/admin/teams"),
  ]);

  if (personResponse.status === 403 || catalogueResponse.status === 403) {
    return (
      <Shell personId={viewer.personId}>
        <h1>Person</h1>
        <div className="empty">
          You do not have <code>people.view</code>. Ask an admin.
        </div>
      </Shell>
    );
  }

  if (personResponse.status === 404) {
    notFound();
  }

  if (!personResponse.ok || !catalogueResponse.ok) {
    return (
      <Shell personId={viewer.personId}>
        <h1>Person</h1>
        <div className="empty">That person could not be loaded.</div>
      </Shell>
    );
  }

  const person = (await personResponse.json()) as PersonDetail;
  const catalogue = (await catalogueResponse.json()) as Catalogue;
  const mine = await currentPermissions(viewer.personId);

  // Expiry is decided here rather than in the browser. The two clocks disagree
  // by whatever the reader's machine is wrong by, and a row that renders as
  // live on the server and lapsed on the client is a hydration mismatch as
  // well as a lie.
  const now = Date.now();
  const lapsed = (expiresAt: string | null) =>
    expiresAt !== null && new Date(expiresAt).getTime() <= now;

  const names = new Map(catalogue.teams.map((team) => [team.slug, team.name]));
  const sensitive = new Set(
    catalogue.permissions.filter((p) => p.sensitive).map((p) => p.value),
  );

  const teams: TeamRow[] = person.teams
    .map((team) => ({
      slug: team.slug,
      name: names.get(team.slug) ?? team.slug,
      expiresAt: team.expiresAt,
      expired: lapsed(team.expiresAt),
    }))
    .sort((a, b) => a.name.localeCompare(b.name));

  const grants: GrantRow[] = person.grants
    .map((row) => ({
      permission: row.permission,
      expiresAt: row.expiresAt,
      expired: lapsed(row.expiresAt),
      sensitive: sensitive.has(row.permission),
    }))
    .sort((a, b) => a.permission.localeCompare(b.permission));

  const onTeams = new Set(person.teams.map((team) => team.slug));
  const held = new Set(person.grants.map((row) => row.permission));

  return (
    <Shell personId={viewer.personId}>
      <Link href="/people" className="back">
        ← People
      </Link>

      <h1>{person.email}</h1>
      <p className="lede">
        {person.kind}
        {" · "}
        <span className={person.revoked ? "pill revoked" : "pill active"}>
          {person.revoked ? "Revoked" : "Active"}
        </span>
        {person.revoked && person.revokedAt ? (
          <span className="meta"> since {person.revokedAt.slice(0, 10)}</span>
        ) : null}
      </p>

      <section className="panel">
        <h2>Effective permissions</h2>
        <p className="meta" style={{ marginBottom: "0.75rem" }}>
          The union of every team baseline they still hold and every grant that
          has not expired. This is exactly what the API checks.
        </p>

        {person.effective.length === 0 ? (
          <p className="meta">
            Nothing. They can sign in and see no screen in this console.
          </p>
        ) : (
          <div className="permissions">
            {person.effective.map((permission) => (
              <span key={permission}>{permission}</span>
            ))}
          </div>
        )}
      </section>

      <div className="columns">
        <Teams
          personId={person.id}
          rows={teams}
          available={catalogue.teams.filter((team) => !onTeams.has(team.slug))}
          canManage={mine.has("people.manage_teams")}
        />

        <Grants
          personId={person.id}
          rows={grants}
          available={catalogue.permissions.filter((p) => !held.has(p.value))}
          canGrant={mine.has("people.grant_permissions")}
        />
      </div>

      {mine.has("people.manage_teams") && !person.revoked ? (
        <Revoke
          personId={person.id}
          email={person.email}
          isSelf={person.id === viewer.personId}
        />
      ) : null}
    </Shell>
  );
}
