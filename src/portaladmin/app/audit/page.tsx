import Link from "next/link";
import { redirect } from "next/navigation";
import {
  apiFetch,
  currentPerson,
  type AuditEntry,
  type Listed,
} from "@/lib/api";
import { Shell } from "../shell";
import { Filters } from "./filters";
import { Trail } from "./trail";

/**
 * Every change anybody has made to anybody's access.
 *
 * The screen the permission model has been promising: `granted_by` says who
 * decided a grant that still exists, and a log line says it for as long as the
 * logs are kept. This says what happened, in order, including the changes that
 * have since been undone — which is the half that answers "they had export
 * last week and now they do not".
 *
 * Rendered on the server, and filtered there too. The trail is unbounded where
 * the people list is tens of rows, so the browser-side filtering the people
 * table uses would mean shipping the whole history to filter a page of it.
 */
export default async function Audit({
  searchParams,
}: {
  searchParams: Promise<{ subject?: string; actor?: string; before?: string }>;
}) {
  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const { subject, actor, before } = await searchParams;

  const query = new URLSearchParams();
  if (subject) query.set("subject", subject);
  if (actor) query.set("actor", actor);
  if (before) query.set("before", before);

  const response = await apiFetch(`/admin/audit?${query}`);

  // 403 is the answer, not a fault. audit.view is separate from people.view
  // because the trail names everyone holding a sensitive permission and when
  // they got it — saying which permission is missing is what turns "it doesn't
  // work" into a request somebody can act on.
  if (response.status === 403) {
    return (
      <Shell personId={person.personId}>
        <h1>Audit</h1>
        <div className="empty">
          You do not have <code>audit.view</code>. Ask an admin.
        </div>
      </Shell>
    );
  }

  // A bad uuid in the query string is a 400 from the API. Saying so beats
  // rendering an empty trail, which reads as "nothing ever happened".
  if (response.status === 400) {
    return (
      <Shell personId={person.personId}>
        <h1>Audit</h1>
        <div className="empty">
          That filter is not a person id.{" "}
          <Link href="/audit">Show everything</Link>.
        </div>
      </Shell>
    );
  }

  if (!response.ok) {
    return (
      <Shell personId={person.personId}>
        <h1>Audit</h1>
        <div className="empty">The trail could not be loaded.</div>
      </Shell>
    );
  }

  const { entries, nextBefore } = (await response.json()) as {
    entries: AuditEntry[];
    nextBefore: number | null;
  };

  // Addresses are resolved here rather than being recorded in the trail.
  // audit.entries holds person ids and nothing else, so that a copy of it is
  // not a copy of the people — and this join is behind people.view, which is a
  // separate gate. Somebody with audit.view alone sees ids, which is enough to
  // follow a thread and not enough to build a mailing list.
  const names = await addressesById();

  return (
    <Shell personId={person.personId}>
      <h1>Audit</h1>
      <p className="lede">
        Every change to what somebody may do, newest first. Written by the
        database inside the same transaction as the change, so nothing here is
        missing because a request failed halfway.
      </p>

      <Filters subject={subject ?? ""} actor={actor ?? ""} />

      {entries.length === 0 ? (
        <div className="empty">
          {subject || actor
            ? "No changes match that."
            : "Nothing has changed anybody's access yet."}
        </div>
      ) : (
        <Trail entries={entries} names={names} />
      )}

      {/* A cursor, not a page number: the trail only grows at the newest end,
          so an offset would show one entry twice and skip another while an
          incident is still producing them. */}
      {nextBefore !== null && entries.length > 0 ? (
        <p className="count">
          <Link
            href={`/audit?${new URLSearchParams({
              ...(subject ? { subject } : {}),
              ...(actor ? { actor } : {}),
              before: String(nextBefore),
            })}`}
          >
            Older →
          </Link>
        </p>
      ) : null}
    </Shell>
  );
}

/**
 * Person id to address, for everybody who can sign in.
 *
 * One request rather than one per entry, because a page of a hundred entries
 * mentions the same handful of admins over and over. Failure is not fatal:
 * without people.view this comes back empty and the screen shows ids, which is
 * less useful and still true.
 */
async function addressesById(): Promise<Map<string, string>> {
  try {
    const response = await apiFetch("/admin/people");
    if (!response.ok) {
      return new Map();
    }

    const { people } = (await response.json()) as { people: Listed[] };
    return new Map(people.map((p) => [p.id, p.email]));
  } catch {
    return new Map();
  }
}
