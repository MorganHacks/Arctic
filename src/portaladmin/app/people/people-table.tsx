"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import type { Listed } from "@/lib/api";

/**
 * The list, filtered in the browser rather than on the server.
 *
 * The whole list arrives in one response because it is people who can sign in
 * — organizers and registered hackers — which is tens of rows, not hundreds.
 * Filtering it here means typing is instant. Pushing the filter into the URL
 * and back through the API would put a round trip on every keystroke to search
 * a list that already fits in memory.
 *
 * The moment that stops being true the fix is paging in the API, not a
 * debounce here.
 */
export function PeopleTable({ people }: { people: Listed[] }) {
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState("all");
  const [status, setStatus] = useState("all");

  const shown = useMemo(() => {
    const needle = query.trim().toLowerCase();

    return people.filter((person) => {
      if (kind !== "all" && person.kind !== kind) {
        return false;
      }

      if (status === "active" && person.revoked) {
        return false;
      }

      if (status === "revoked" && !person.revoked) {
        return false;
      }

      if (needle === "") {
        return true;
      }

      // Teams are searchable alongside the address, because "who is on comms"
      // is asked as often as "where is this person" and neither deserves its
      // own control.
      return (
        person.email.toLowerCase().includes(needle) ||
        person.teams.some((team) => team.includes(needle))
      );
    });
  }, [people, query, kind, status]);

  return (
    <>
      <div className="filters">
        <div className="grow">
          <label htmlFor="q">Search</label>
          <input
            id="q"
            type="search"
            className="grow"
            placeholder="Address or team"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            style={{ width: "100%" }}
          />
        </div>

        <div>
          <label htmlFor="kind">Kind</label>
          <select
            id="kind"
            value={kind}
            onChange={(event) => setKind(event.target.value)}
          >
            <option value="all">Everyone</option>
            <option value="organizer">Organizers</option>
            <option value="hacker">Hackers</option>
          </select>
        </div>

        <div>
          <label htmlFor="status">Status</label>
          <select
            id="status"
            value={status}
            onChange={(event) => setStatus(event.target.value)}
          >
            <option value="all">Any</option>
            <option value="active">Active</option>
            <option value="revoked">Revoked</option>
          </select>
        </div>
      </div>

      {shown.length === 0 ? (
        <div className="empty">Nobody matches that.</div>
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
            {shown.map((person) => (
              <tr key={person.id}>
                <td>
                  <Link href={`/people/${person.id}`}>{person.email}</Link>
                </td>
                <td>{person.kind}</td>
                <td>
                  {person.teams.length > 0 ? person.teams.join(", ") : "—"}
                </td>
                <td>
                  <span className={person.revoked ? "pill revoked" : "pill active"}>
                    {person.revoked ? "Revoked" : "Active"}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <p className="count">
        {shown.length} of {people.length}
      </p>
    </>
  );
}
