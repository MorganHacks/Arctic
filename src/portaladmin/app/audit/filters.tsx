import Link from "next/link";

/**
 * The two questions the trail answers, as a form.
 *
 * A plain GET form rather than a client component with state. The filters
 * belong in the URL: an organizer reviewing somebody's access sends that link
 * to the admin who has to act on it, and a filter held in React state is one
 * that cannot be sent to anybody.
 *
 * `before` is deliberately not carried across a new search. Changing the
 * filter and keeping the old cursor would land the reader in the middle of a
 * different trail, at a position that means nothing.
 */
export function Filters({ subject, actor }: { subject: string; actor: string }) {
  return (
    <form method="get" action="/audit" className="filters">
      <div className="grow">
        <label htmlFor="subject">Done to</label>
        <input
          id="subject"
          name="subject"
          type="search"
          className="mono"
          placeholder="Person id"
          defaultValue={subject}
          style={{ width: "100%" }}
        />
      </div>

      <div className="grow">
        <label htmlFor="actor">Done by</label>
        <input
          id="actor"
          name="actor"
          type="search"
          className="mono"
          placeholder="Person id"
          defaultValue={actor}
          style={{ width: "100%" }}
        />
      </div>

      <button type="submit">Filter</button>

      {subject || actor ? (
        <Link href="/audit" className="button">
          Clear
        </Link>
      ) : null}
    </form>
  );
}
