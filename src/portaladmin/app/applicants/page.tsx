import { ApplicantsTable } from "@/components/applicants/applicants-table";
import { EmptyState } from "@/components/ui/empty-state";
import { Filters } from "@/components/applicants/filters";
import { STATUSES } from "@/components/applicants/status";
import styles from "@/components/applicants/applicants-list.module.css";
import type { Status } from "@/components/applicants/types";
import { readPageData } from "@/lib/page-data";
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
  const { event, q, status } = await searchParams;

  const asked = status === undefined ? [] : [status].flat();
  const known = STATUSES as string[];
  const invalid = asked.some((one) => !known.includes(one));
  const filter: Filter = { event, q, status: asked as Status[] };
  const { person, data: read } = await readPageData(() => invalid ? Promise.resolve(null) : readView(filter));

  // Refused rather than quietly dropped. A status we do not recognise in the
  // URL means the reader is looking at something other than what they asked
  // for, and a list that silently widened itself is worse than one that says
  // it cannot.
  if (!read) {
    return (
      <Denied personId={person.personId}>That filter is not one of ours.</Denied>
    );
  }

  if (!read.ok) {
    return <Denied personId={person.personId}>{read.error}</Denied>;
  }

  const { events, chosen, counts, items, nextCursor } = read.view;

  if (!chosen) {
    return (
      <Shell personId={person.personId}>
        <h1>Applicants</h1>
        <EmptyState variant="data" size="page" title="No applicants yet"
          description="Create an event under Events. Applicants will appear here once a form collects them." />
      </Shell>
    );
  }

  return (
    <Shell personId={person.personId}>
      <div className={styles.page}>
        <div className={styles.head}>
          <h1>Applicants</h1>
          <p>Review applications for your event.</p>
        </div>

        <Filters
          events={events}
          chosen={chosen}
          q={q ?? ""}
          statuses={filter.status ?? []}
          counts={counts}
        />

        {items.length === 0 ? (
          <div className={styles.empty}>
            <EmptyState variant="data" size="page"
              title={q || asked.length > 0 ? "No applicants found" : "No applicants yet"}
              description={q || asked.length > 0 ? "Try another search or adjust your filters." : "Applications for this event will appear here as they arrive."} />
          </div>
        ) : (
          <ApplicantsTable
            key={`${chosen.id}:${q ?? ""}:${[...new Set(asked)].sort().join(",")}`}
            initialItems={items}
            initialCursor={nextCursor}
            total={q ? null : (asked.length ? [...new Set(asked)] as Status[] : STATUSES).reduce((sum, one) => sum + (counts[one] ?? 0), 0)}
            // Bound to this filter on the server, so the next page is a page of
            // the same list. A cursor read against a different filter would
            // start somewhere that means nothing.
            loadMore={loadApplicants.bind(null, { ...filter, event: chosen.id })}
          />
        )}
      </div>
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
