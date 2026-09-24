import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { ArrowLeft01Icon, ArrowRight01Icon, Audit02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { apiFetch, type AuditEntry, type Listed } from "@/lib/api";
import { readPageData } from "@/lib/page-data";
import { Shell } from "../shell";
import { Filters, ranges } from "./filters";
import { Trail } from "./trail";
import memberStyles from "../people/members.module.css";
import styles from "./audit.module.css";

const pageSize = 20;

export default async function Audit({ searchParams }: {
  searchParams: Promise<{ subject?: string; actor?: string; before?: string; action?: string; range?: string }>;
}) {
  const { subject = "", actor = "", before = "", action = "", range = "" } = await searchParams;
  const validRange = Object.hasOwn(ranges, range);
  const filters = Object.fromEntries(Object.entries({ subject, actor, action, range }).filter(([, value]) => value));
  const query = new URLSearchParams({ ...filters, limit: String(pageSize + 1) });
  query.delete("range");
  if (before) query.set("before", before);
  if (range && validRange) query.set("since", new Date(Date.now() - Number(range) * 86400000).toISOString());

  const { person, data: [response, directory] } = await readPageData(() => Promise.all([
    validRange ? apiFetch(`/admin/audit?${query}`) : null,
    validRange ? peopleDirectory() : [],
  ]));
  let message = "";
  if (!validRange || response?.status === 400) message = "That filter is invalid. Clear the filters to see all activity.";
  else if (response?.status === 403) message = "You do not have permission to view the audit log. Ask an admin for audit.view access.";
  else if (!response?.ok) message = "The audit log could not be loaded. Please try again.";

  let entries: AuditEntry[] = [];
  let people: Listed[] = [];
  if (response?.ok) {
    entries = ((await response.json()) as { entries: AuditEntry[] }).entries;
    people = directory;
  }
  const shown = entries.slice(0, pageSize);
  const hasOlder = entries.length > pageSize;
  const latestHref = `/audit?${new URLSearchParams(filters)}`;
  const olderHref = hasOlder ? `/audit?${new URLSearchParams({ ...filters, before: String(shown[shown.length - 1].id) })}` : null;

  return (
    <Shell personId={person.personId}>
      <section className={`${memberStyles.page} ${styles.page}`} aria-labelledby="audit-title">
        <div className={styles.controls}>
          <header className={styles.header}>
            <h1 id="audit-title" className={styles.title}>Audit log</h1>
            <p className={styles.description}>
              Every access change, automatically recorded and shown newest first.
            </p>
          </header>
          {!message ? <Filters key={JSON.stringify(filters)} subject={subject} actor={actor} action={action} range={range} people={people} /> : null}
        </div>
        <div className={styles.activity} role="region" aria-label="Audit log activity" tabIndex={0}>
          {message ? (
            <div className={styles.empty}>
              <Icon icon={Audit02Icon} size={24} />
              <p>{message}</p>
              {response?.status === 400 || !validRange ? <Link href="/audit">Clear filters</Link> : null}
            </div>
          ) : (
            <>
              {shown.length ? <Trail entries={shown} people={people} filters={filters} /> : (
                <div className={styles.empty}>
                  <Icon icon={Audit02Icon} size={24} />
                  <p>{Object.keys(filters).length || before ? "No activity matches these filters." : "No activity yet."}</p>
                  {Object.keys(filters).length || before ? <Link href="/audit">View all activity</Link> : null}
                </div>
            )}
            <footer className={styles.footer}>
              <nav className={styles.pagination} aria-label="Audit log pages">
                {olderHref ? (
                  <Link href={olderHref} className={styles.pageButton}><Icon icon={ArrowLeft01Icon} size={16} />Older</Link>
                ) : <button type="button" className={styles.pageButton} disabled><Icon icon={ArrowLeft01Icon} size={16} />Older</button>}
                {before ? (
                  <Link href={latestHref} className={styles.pageButton}>Newest<Icon icon={ArrowRight01Icon} size={16} /></Link>
                ) : <button type="button" className={styles.pageButton} disabled>Newest<Icon icon={ArrowRight01Icon} size={16} /></button>}
              </nav>
              <p className={styles.count}>{shown.length} {shown.length === 1 ? "event" : "events"}</p>
            </footer>
          </>
          )}
        </div>
      </section>
    </Shell>
  );
}

async function peopleDirectory(): Promise<Listed[]> {
  try {
    const response = await apiFetch("/admin/people");
    if (!response.ok) return [];
    return ((await response.json()) as { people: Listed[] }).people;
  } catch {
    return [];
  }
}
