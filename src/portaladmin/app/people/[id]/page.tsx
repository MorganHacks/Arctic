import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import {
  apiFetch,
  currentPerson,
  type Catalogue,
  type PersonDetail,
} from "@/lib/api";
import { Shell } from "../../shell";
import styles from "../people.module.css";
import { Grants, Revoke, Teams, type GrantRow, type TeamRow } from "./controls";
import { Effective, type EffectiveRow, type Source } from "./provenance";

/**
 * One person, and the controls for changing what they can do.
 *
 * Everything on this screen answers one question: why can this person do what
 * they can do. Teams, grants and the union of the two are shown together
 * because the answer is almost never in one of them alone — "they lost export
 * when their judge membership lapsed" is only readable if the lapsed
 * membership is still on the page.
 *
 * ## Where provenance comes from
 *
 * The API does not send it. `/admin/people/{id}` sends the memberships and the
 * grants, each with its expiry, and a flat list of effective permission
 * strings; `/admin/teams` sends each team's baseline. Provenance is the join of
 * those three, and it is exact rather than a guess, because the rule being
 * inverted is the whole of the rule: effective permissions are the union of
 * every live team baseline and every live grant, additively, with no deny and
 * no precedence. There is nothing else that could have granted a permission,
 * so attributing one to the sources that hold it cannot be wrong.
 *
 * It is done here, on the server, against the same two responses the page
 * already fetches. That matters for the expiries: the browser's clock is wrong
 * by whatever the reader's machine is wrong by, and a row that renders live on
 * the server and lapsed on the client is a hydration mismatch as well as a lie.
 *
 * What the API does *not* send, and what this screen therefore does not claim:
 * who made a grant, when they made it, and any note attached to it. The audit
 * trail records all three — the `/audit` screen reads them — but nothing on
 * this endpoint carries them, so nothing here shows them.
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
  const mine = viewer.permissions;

  // Expiry is decided here rather than in the browser. The two clocks disagree
  // by whatever the reader's machine is wrong by, and a row that renders as
  // live on the server and lapsed on the client is a hydration mismatch as
  // well as a lie.
  const now = Date.now();
  const lapsed = (expiresAt: string | null) =>
    expiresAt !== null && new Date(expiresAt).getTime() <= now;

  const names = new Map(catalogue.teams.map((team) => [team.slug, team.name]));
  const baselines = new Map(
    catalogue.teams.map((team) => [team.slug, team.permissions]),
  );
  const sensitive = new Set(
    catalogue.permissions.filter((p) => p.sensitive).map((p) => p.value),
  );

  const teams: TeamRow[] = person.teams
    .map((team) => ({
      slug: team.slug,
      name: names.get(team.slug) ?? team.slug,
      expiresAt: team.expiresAt,
      expired: lapsed(team.expiresAt),
      // The baseline the membership confers, shown under it. It is what makes
      // removing somebody from a team a decision rather than a click.
      permissions: [...(baselines.get(team.slug) ?? [])].sort(),
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

  /*
   * Only what is still granting something.
   *
   * A lapsed membership is kept on the page — it is the explanation for a
   * permission that is gone — but it is not a source of one that is present,
   * and listing it as though it were would be the page contradicting the gate.
   */
  const liveTeams = teams.filter((team) => !team.expired);
  const liveGrants = grants.filter((row) => !row.expired);

  const effective: EffectiveRow[] = person.effective.map((permission) => {
    const sources: Source[] = [
      ...liveTeams
        .filter((team) => team.permissions.includes(permission))
        .map((team) => ({
          kind: "team" as const,
          label: team.name,
          expiresAt: team.expiresAt,
        })),
      ...liveGrants
        .filter((row) => row.permission === permission)
        .map((row) => ({
          kind: "grant" as const,
          label: null,
          expiresAt: row.expiresAt,
        })),
    ];

    /*
     * When it runs out, which is the latest of its sources and not the
     * earliest.
     *
     * The union is over sources, so a permission survives as long as any one
     * route to it survives. Somebody on Comms until March who also holds a
     * direct grant until February keeps it until March, and a page that showed
     * February would have an admin re-granting something that was never going
     * to lapse. One permanent source makes the whole row permanent.
     */
    const until = sources.some((source) => source.expiresAt === null)
      ? null
      : sources
          .map((source) => source.expiresAt)
          .filter((at): at is string => at !== null)
          .sort()
          .at(-1) ?? null;

    return {
      permission,
      sources,
      until,
      // Never expiring and no source at all are different facts. The second is
      // only reachable if a team's baseline changed between the two requests
      // that built this page, and it is shown rather than smoothed over.
      permanent: sources.length > 0 && until === null,
      sensitive: sensitive.has(permission),
    };
  });

  const onTeams = new Set(person.teams.map((team) => team.slug));
  const held = new Set(person.grants.map((row) => row.permission));

  return (
    <Shell personId={viewer.personId}>
      <Link href="/people" className="back">
        ← People
      </Link>

      <h1 className={styles.address}>{person.email}</h1>
      <p className={styles.identity}>
        {person.kind}
        {" · "}
        <span className={person.revoked ? "pill revoked" : "pill active"}>
          {person.revoked ? "Revoked" : "Active"}
        </span>
        {person.revoked && person.revokedAt ? (
          <span className="meta"> since {person.revokedAt.slice(0, 10)}</span>
        ) : null}
      </p>

      <Effective rows={effective} />

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
