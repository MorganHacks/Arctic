import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { AddCircleIcon, ArrowDown01Icon, Edit02Icon, RemoveCircleIcon } from "@hugeicons/core-free-icons";
import { Avatar } from "@/components/ui/avatar";
import { Icon } from "@/components/ui/icon";
import type { AuditEntry, Listed } from "@/lib/api";
import { displayName } from "@/lib/person-profile";
import { events } from "./filters";
import styles from "./audit.module.css";

const additions = new Set(["organizer.added", "person.restored", "team.joined", "grant.added", "baseline.added"]);
const removals = new Set(["person.revoked", "team.left", "grant.removed", "baseline.removed"]);
const dateFormat = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" });
const timeFormat = new Intl.DateTimeFormat("en-US", { hour: "2-digit", minute: "2-digit", second: "2-digit", timeZone: "UTC" });

export function Trail({ entries, people, filters }: {
  entries: AuditEntry[];
  people: Listed[];
  filters: Record<string, string>;
}) {
  const names = new Map(people.map((person) => [person.id, person]));

  return (
    <ol className={styles.timeline} aria-label="Audit events">
      {entries.map((entry) => {
        const added = additions.has(entry.action);
        const removed = removals.has(entry.action);
        const occurred = new Date(entry.occurredAt);
        const icon = added ? AddCircleIcon : removed ? RemoveCircleIcon : Edit02Icon;

        return (
          <li key={entry.id} className={styles.entry}>
            <span className={`${styles.marker} ${added ? styles.added : removed ? styles.removed : styles.changed}`}>
              <Icon icon={icon} size={20} />
            </span>
            <details className={styles.event}>
              <summary className={styles.summary}>
                <time className={styles.timestamp} dateTime={entry.occurredAt}>
                  <span>{dateFormat.format(occurred)}</span>
                  <span>{timeFormat.format(occurred)}</span>
                  <span className={styles.timezone}>UTC</span>
                </time>
                <span className={styles.eventLine}>
                  <strong className={styles.eventName}>{events[entry.action] ?? entry.action}</strong>
                  {entry.target ? <span className={styles.target}>{entry.target}</span> : null}
                  <span className={styles.muted}>{entry.subjectId ? "for" : "in"}</span>
                  {entry.subjectId ? (
                    <Person id={entry.subjectId} names={names} filter="subject" filters={filters} />
                  ) : <span className={styles.team}>{entry.subjectTeam}</span>}
                  <span className={styles.muted}>by</span>
                  {entry.actorId ? (
                    <Person id={entry.actorId} names={names} filter="actor" filters={filters} avatar />
                  ) : <span className={styles.muted}>No actor recorded</span>}
                </span>
                <Icon icon={ArrowDown01Icon} size={14} className={styles.detailChevron} />
              </summary>
              <div className={styles.details}>
                <dl>
                  <div><dt>Event</dt><dd>{entry.action}</dd></div>
                  <div><dt>Recorded</dt><dd>{dateFormat.format(occurred)} at {timeFormat.format(occurred)} UTC</dd></div>
                  <div><dt>Actor</dt><dd>{entry.actorId ? names.get(entry.actorId)?.email ?? entry.actorId : "No actor recorded"}</dd></div>
                  <div><dt>{entry.subjectId ? "Person" : "Team"}</dt><dd>{entry.subjectId ? names.get(entry.subjectId)?.email ?? entry.subjectId : entry.subjectTeam}</dd></div>
                  {entry.target ? <div><dt>Target</dt><dd>{entry.target}</dd></div> : null}
                  <div><dt>Expires</dt><dd>{entry.expiresAt ? `${dateFormat.format(new Date(entry.expiresAt))} at ${timeFormat.format(new Date(entry.expiresAt))} UTC` : "No expiration recorded"}</dd></div>
                </dl>
                {Object.keys(entry.detail).length > 0 ? <pre className={styles.payload}>{JSON.stringify(entry.detail, null, 2)}</pre> : null}
              </div>
            </details>
          </li>
        );
      })}
    </ol>
  );
}

function Person({ id, names, filter, filters, avatar = false }: {
  id: string;
  names: Map<string, Listed>;
  filter: "actor" | "subject";
  filters: Record<string, string>;
  avatar?: boolean;
}) {
  const person = names.get(id);
  const name = person ? displayName(person.fullName, person.email) : id.slice(0, 8);
  const query = new URLSearchParams({ ...filters, [filter]: id });

  return (
    <Link href={`/audit?${query}`} title={person?.email ?? id} className={styles.person}>
      <span>{name}</span>
      {avatar && person ? <Avatar name={name} email={person.email} avatarUrl={person.avatarUrl} className={styles.avatar} /> : null}
    </Link>
  );
}
