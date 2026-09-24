import Form from "next/form";
import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { ArrowDown01Icon, Clock01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import type { Listed } from "@/lib/api";
import { displayName } from "@/lib/person-profile";
import styles from "./audit.module.css";

export const events: Record<string, string> = {
  "organizer.added": "Organizer added",
  "person.revoked": "Access revoked",
  "person.restored": "Access restored",
  "team.joined": "Team joined",
  "team.left": "Team left",
  "team.retimed": "Membership updated",
  "grant.added": "Permission granted",
  "grant.changed": "Permission updated",
  "grant.removed": "Permission removed",
  "baseline.added": "Team permission added",
  "baseline.removed": "Team permission removed",
};

export const ranges: Record<string, string> = {
  "": "All time",
  "1": "Last 24 hours",
  "7": "Last 7 days",
  "30": "Last 30 days",
  "90": "Last 90 days",
};

export function Filters({ subject, actor, action, range, people }: {
  subject: string;
  actor: string;
  action: string;
  range: string;
  people: Listed[];
}) {
  const filtered = Boolean(subject || actor || action || range);

  return (
    <Form action="/audit" className={styles.filters}>
      <div className={`${styles.selectField} ${styles.dateField}`}>
        <Icon icon={Clock01Icon} size={18} />
        <select name="range" aria-label="Date range" defaultValue={range}>
          <option value="">All time</option>
          {Object.entries(ranges).filter(([value]) => value).map(([value, label]) => (
            <option key={value} value={value}>{label}</option>
          ))}
        </select>
        <Icon icon={ArrowDown01Icon} size={14} className={styles.chevron} />
      </div>
      <div className={styles.filterGroup}>
        <div className={styles.selectField}>
          <select name="action" aria-label="Event type" defaultValue={action}>
            <option value="">All events</option>
            {action && !events[action] ? <option value={action}>{action}</option> : null}
            {Object.entries(events).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
          <Icon icon={ArrowDown01Icon} size={14} className={styles.chevron} />
        </div>
        <PersonFilter name="subject" label="Affected person" placeholder="All people" value={subject} people={people} />
        <PersonFilter name="actor" label="Actor" placeholder="All actors" value={actor} people={people} />
        <button type="submit" className={styles.filterButton}>Apply</button>
        {filtered ? <Link href="/audit" className={styles.clear}>Clear</Link> : null}
      </div>
    </Form>
  );
}

function PersonFilter({ name, label, placeholder, value, people }: {
  name: string;
  label: string;
  placeholder: string;
  value: string;
  people: Listed[];
}) {
  if (people.length === 0) {
    return <input className={styles.personInput} type="search" name={name} aria-label={label} placeholder={`${label} ID`} defaultValue={value} />;
  }

  return (
    <div className={`${styles.selectField} ${styles.personField}`}>
      <select name={name} aria-label={label} defaultValue={value}>
        <option value="">{placeholder}</option>
        {value && !people.some((person) => person.id === value) ? <option value={value}>{value}</option> : null}
        {people.map((person) => (
          <option key={person.id} value={person.id}>{displayName(person.fullName, person.email)}</option>
        ))}
      </select>
      <Icon icon={ArrowDown01Icon} size={14} className={styles.chevron} />
    </div>
  );
}
