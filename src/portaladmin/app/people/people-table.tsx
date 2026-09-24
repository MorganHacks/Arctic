"use client";

import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { useMemo, useState } from "react";
import { Search01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { Avatar } from "@/components/ui/avatar";
import { displayName } from "@/lib/person-profile";
import type { Listed } from "@/lib/api";
import styles from "./members.module.css";

const kinds = [
  { value: "all", label: "Everyone" },
  { value: "organizer", label: "Organizers" },
  { value: "hacker", label: "Hackers" },
] as const;

/** The small account directory is filtered locally, including team slugs. */
export function PeopleTable({ people }: { people: Listed[] }) {
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState("all");

  const shown = useMemo(() => {
    const needle = query.trim().toLowerCase();
    return people.filter((person) => {
      if (kind !== "all" && person.kind !== kind) return false;
      return (
        person.email.toLowerCase().includes(needle) ||
        (person.fullName?.toLowerCase().includes(needle) ?? false) ||
        person.teams.some((team) => team.toLowerCase().includes(needle))
      );
    });
  }, [people, query, kind]);

  return (
    <section className={styles.section} aria-labelledby="people-title">
      <div className={styles.tabs} role="group" aria-label="Filter people by type">
        {kinds.map((item) => (
          <button
            key={item.value}
            type="button"
            className={styles.tab}
            aria-pressed={kind === item.value}
            onClick={() => setKind(item.value)}
          >
            {item.label}
          </button>
        ))}
      </div>

      <div className={styles.membersHead}>
        <h1 id="people-title" className={styles.title}>Members</h1>
        <div className={styles.search}>
          <Icon icon={Search01Icon} size={18} />
          <input
            aria-label="Search people by name, email, or team"
            type="search"
            autoComplete="off"
            placeholder="Search members"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />
        </div>
      </div>

      <div className={styles.tableFrame}>
        <table className={styles.table} aria-label="People">
          <colgroup>
            <col />
            <col className={styles.roleColumn} />
            <col className={styles.statusColumn} />
          </colgroup>
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Role</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            {shown.map((person) => {
              const name = displayName(person.fullName, person.email);
              return (
                <tr key={person.id}>
                  <td>
                    <Link
                      href={`/people/${person.id}`}
                      className={styles.member}
                      aria-label={`Manage ${person.email}`}
                    >
                      <Avatar
                        className={styles.avatar}
                        name={name}
                        email={person.email}
                        avatarUrl={person.avatarUrl}
                      />
                      <span className={styles.memberMeta}>
                        <strong>{name}</strong>
                        <span>{person.email}</span>
                      </span>
                    </Link>
                  </td>
                  <td>
                    <div className={styles.role}>
                      <span>{person.kind === "organizer" ? "Organizer" : person.kind === "hacker" ? "Hacker" : person.kind}</span>
                      {person.teams.length > 0 ? (
                        <span className={styles.teams}>{person.teams.join(", ")}</span>
                      ) : null}
                    </div>
                  </td>
                  <td>
                    <span className={person.revoked ? styles.revoked : styles.active}>
                      {person.revoked ? "Revoked" : "Active"}
                    </span>
                  </td>
                </tr>
              );
            })}
            {shown.length === 0 ? (
              <tr>
                <td colSpan={3} className={styles.empty}>
                  {people.length === 0 ? "No people yet." : "No members found."}
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
      <p className={styles.visuallyHidden} role="status">
        {shown.length} of {people.length} people
      </p>
    </section>
  );
}
