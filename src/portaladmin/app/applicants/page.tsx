import { redirect } from "next/navigation";
import { ApplicantsTable } from "@/components/applicants/applicants-table";
import { Filters } from "@/components/applicants/filters";
import { STATUSES } from "@/components/applicants/status";
import styles from "@/components/applicants/applicants.module.css";
import type { Status } from "@/components/applicants/types";
import { currentPerson } from "@/lib/api";
import { Shell } from "../shell";
import { loadApplicants } from "./actions";
import { readView, type Filter } from "./api";

/**
 * Everybody who has applied.
 *
 * The screen registration lives in while applications are open. It answers
 * three questions and nothing else: who has applied, where have they got to,
 * and which one do I open next. Everything about one person is on their own
 * page, one click away on every row.
 *
 * Deliberately not the responses table on the forms screen, which is the same
 * rows arranged by question. That one is for reading what people answered;
 * this one is for working through them.
 *
 * The filters are all in the URL and the first page is rendered on the server.
 * Only "load more" happens in the browser, so a reader who has turned six
 * pages keeps them when they open a row and come back.
 */
export default async function Applicants({
  searchParams,
}: {
  searchParams: Promise<{ event?: string; q?: string; status?: string | string[] }>;
}) {
  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const { event, q, status } = await searchParams;

  const asked = status === undefined ? [] : [status].flat();
  const known = STATUSES as string[];

  // Refused rather than quietly dropped. A status we do not recognise in the
  // URL means the reader is looking at something other than what they asked
  // for, and a list that silently widened itself is worse than one that says
  // it cannot.
  if (asked.some((one) => !known.includes(one))) {
    return (
      <Denied personId={person.personId}>That filter is not one of ours.</Denied>
    );
  }

  const filter: Filter = {
    event,
    q,
    status: asked as Status[],
  };

  const read = await readView(filter);

  if (!read.ok) {
    return <Denied personId={person.personId}>{read.error}</Denied>;
  }

  const { events, chosen, counts, items, nextCursor } = read.view;

  if (!chosen) {
    return (
      <Denied personId={person.personId}>
        There is no event yet. One is made by hand, once a year.
      </Denied>
    );
  }

  return (
    <Shell personId={person.personId}>
      {/* Which event this is, beside the heading rather than inside the filter
          bar. It is not a filter — it is what the whole screen is about, and
          every count under it is a count about this one event. */}
      <div className={styles.head}>
        <h1>Applicants</h1>
        <p className={styles.scope}>{chosen.name}</p>
      </div>

      <Filters
        events={events}
        chosen={chosen}
        q={q ?? ""}
        statuses={filter.status ?? []}
        counts={counts}
      />

      {items.length === 0 ? (
        <div className="empty">
          {q || asked.length > 0
            ? "Nothing matches that."
            : "Nobody has applied yet."}
        </div>
      ) : (
        <ApplicantsTable
          initialItems={items}
          initialCursor={nextCursor}
          // Bound to this filter on the server, so the next page is a page of
          // the same list. A cursor read against a different filter would
          // start somewhere that means nothing.
          loadMore={loadApplicants.bind(null, filter)}
        />
      )}
    </Shell>
  );
}

/** Why there is nothing here, said plainly. */
function Denied({
  personId,
  children,
}: {
  personId: string;
  children: React.ReactNode;
}) {
  return (
    <Shell personId={personId}>
      <h1>Applicants</h1>
      <div className="empty">{children}</div>
    </Shell>
  );
}
