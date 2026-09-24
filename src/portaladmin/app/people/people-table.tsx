"use client";

import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { useMemo, useRef, useState } from "react";
import { ArrowLeft01Icon, ArrowRight01Icon, Search01Icon } from "@hugeicons/core-free-icons";
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

const pageSize = 10;
const numbers = new Intl.NumberFormat("en-US");

/** The small account directory is filtered locally, including team slugs. */
export function PeopleTable({ people }: { people: Listed[] }) {
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState("all");
  const [page, setPage] = useState(0);
  const tableScroll = useRef<HTMLDivElement>(null);

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

  const pageCount = Math.max(1, Math.ceil(shown.length / pageSize));
  const currentPage = Math.min(page, pageCount - 1);
  const start = currentPage * pageSize;
  const end = Math.min(start + pageSize, shown.length);
  const pageRows = shown.slice(start, end);

  function changePage(nextPage: number) {
    setPage(nextPage);
    tableScroll.current?.scrollTo({ top: 0 });
  }

  return (
    <section className={styles.section} aria-labelledby="people-title">
      <div className={styles.tabs} role="group" aria-label="Filter people by type">
        {kinds.map((item) => (
          <button
            key={item.value}
            type="button"
            className={styles.tab}
            aria-pressed={kind === item.value}
            onClick={() => { setKind(item.value); changePage(0); }}
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
            onChange={(event) => { setQuery(event.target.value); changePage(0); }}
          />
        </div>
      </div>

      <div className={styles.tableFrame}>
        <div ref={tableScroll} className={styles.tableScroll} role="region" aria-label="Members table" tabIndex={0}>
          <table id="people-table" className={styles.table} aria-label="People">
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
              {pageRows.map((person) => {
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
        {pageCount > 1 ? (
          <nav className={styles.pagination} aria-label="Members pagination">
            <span role="status" aria-live="polite" aria-atomic="true">
              {numbers.format(start + 1)}-{numbers.format(end)} of {numbers.format(shown.length)}
            </span>
            <div className={styles.paginationActions}>
              <button type="button" aria-label="Previous members page" title="Previous" aria-controls="people-table"
                disabled={currentPage === 0} onClick={() => changePage(currentPage - 1)}>
                <Icon icon={ArrowLeft01Icon} size={16} />
              </button>
              <span aria-label={`Page ${currentPage + 1} of ${pageCount}`}>{numbers.format(currentPage + 1)} / {numbers.format(pageCount)}</span>
              <button type="button" aria-label="Next members page" title="Next" aria-controls="people-table"
                disabled={currentPage + 1 >= pageCount} onClick={() => changePage(currentPage + 1)}>
                <Icon icon={ArrowRight01Icon} size={16} />
              </button>
            </div>
          </nav>
        ) : null}
      </div>
    </section>
  );
}
