import Link from "next/link";
import type { AuditEntry } from "@/lib/api";

/**
 * What each action means, in words somebody reading at 2am can act on.
 *
 * Written out rather than derived from the string, because `grant.added`
 * reversed into "grant added" loses the direction — who gained and who gave —
 * which is the entire content of the entry. The map falls back to the raw
 * action, so an action the API starts recording before this file knows about
 * it shows up unlabelled rather than not at all.
 */
const VERBS: Record<string, string> = {
  "organizer.added": "added to the allowlist",
  "person.revoked": "access revoked",
  "person.restored": "access restored",
  "team.joined": "joined",
  "team.left": "left",
  "team.retimed": "membership retimed on",
  "grant.added": "granted",
  "grant.changed": "grant changed",
  "grant.removed": "grant removed",
  "baseline.added": "baseline gained",
  "baseline.removed": "baseline lost",
};

/**
 * Which actions widen access rather than narrow it.
 *
 * Coloured because it changes what you do next, never for decoration: a
 * reviewer scanning a week of entries is looking for the ones that gave
 * somebody something. Removals are left uncoloured — undoing access is the
 * safe direction, and colouring both would leave nothing standing out.
 */
const WIDENS = new Set([
  "organizer.added",
  "person.restored",
  "team.joined",
  "grant.added",
  "baseline.added",
]);

export function Trail({
  entries,
  names,
}: {
  entries: AuditEntry[];
  names: Map<string, string>;
}) {
  return (
    <table>
      <thead>
        <tr>
          <th>When</th>
          <th>Who</th>
          <th>Did what</th>
          <th>To whom</th>
          <th>Until</th>
        </tr>
      </thead>
      <tbody>
        {entries.map((entry) => (
          <tr key={entry.id}>
            <td className="mono">{entry.occurredAt.slice(0, 19).replace("T", " ")}</td>

            <td>
              {/* Null is a real answer and the screen says so plainly. The
                  seed, an import, a fix run in psql: all genuinely have nobody
                  behind them, and a label like "system" would read as a
                  service account that exists. */}
              {entry.actorId ? (
                <Person id={entry.actorId} names={names} filter="actor" />
              ) : (
                <span className="meta">no actor</span>
              )}
            </td>

            <td>
              <span className={WIDENS.has(entry.action) ? "pill widened" : "pill narrowed"}>
                {VERBS[entry.action] ?? entry.action}
              </span>
              {entry.target ? (
                <>
                  {" "}
                  <code>{entry.target}</code>
                </>
              ) : null}
            </td>

            <td>
              {entry.subjectId ? (
                <Person id={entry.subjectId} names={names} filter="subject" />
              ) : (
                // A baseline change has a team as its subject, not a person:
                // it changes what everybody on that team may do at once,
                // without touching a single person's row.
                <>
                  team <code>{entry.subjectTeam}</code>
                </>
              )}
            </td>

            <td>
              {entry.expiresAt ? (
                <span className="meta">{entry.expiresAt.slice(0, 10)}</span>
              ) : (
                <span className="meta">—</span>
              )}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/**
 * A person, by address where we could resolve one and by id where we could not.
 *
 * The id is what the trail actually stores, so it is what the link filters on.
 * Clicking a name is how a reviewer follows a thread — from "who did this" to
 * "what else did they do" — without retyping a uuid.
 */
function Person({
  id,
  names,
  filter,
}: {
  id: string;
  names: Map<string, string>;
  filter: "actor" | "subject";
}) {
  const known = names.get(id);

  return (
    <Link href={`/audit?${filter}=${id}`} title={id}>
      {known ?? <span className="mono">{id.slice(0, 8)}</span>}
    </Link>
  );
}
