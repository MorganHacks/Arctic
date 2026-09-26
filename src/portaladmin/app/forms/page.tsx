import { apiFetch, type FormsView } from "@/lib/api";
import { FormsList } from "@/components/formslist/forms-list";
import { EmptyState } from "@/components/ui/empty-state";
import { readPageData } from "@/lib/page-data";
import { Shell } from "../shell";

/**
 * The forms on one event.
 *
 * The screen answers three questions and nothing else: what is the link, is it
 * live, and where do I go to edit it. Anything a form has that is not one of
 * those belongs in the builder, which is one press away on every row.
 */
export default async function Forms({
  searchParams,
}: {
  searchParams: Promise<{ event?: string }>;
}) {
  const { event } = await searchParams;
  const { person, data: response } = await readPageData(() => apiFetch(
    `/admin/forms${event ? `?eventId=${encodeURIComponent(event)}` : ""}`,
  ));

  if (response.status === 403) {
    return (
      <Shell personId={person.personId}>
        <h1>Forms</h1>
        <div className="empty">
          You do not have <code>applications.view</code>. Ask an admin.
        </div>
      </Shell>
    );
  }

  if (!response.ok) {
    return (
      <Shell personId={person.personId}>
        <h1>Forms</h1>
        <div className="empty">Forms could not be loaded.</div>
      </Shell>
    );
  }

  const { events, chosen, forms } = (await response.json()) as FormsView;

  // Cosmetic. The API refuses the write whether or not this panel rendered, so
  // hiding it is a courtesy to somebody who cannot use it rather than a control
  // over anything.
  const mine = person.permissions;

  if (!chosen) {
    return (
      <Shell personId={person.personId}>
        <h1>Forms</h1>
        <EmptyState variant="canvas" size="page" title="Create an event first"
          description="A form belongs to an event. Create one under Events to start collecting responses." />
      </Shell>
    );
  }

  /*
   * The clock is read once, here, and handed down.
   *
   * A form that closes while this page is being rendered would otherwise
   * be able to come out Live in one row's reckoning and Closed in the
   * next, which is a bug nobody would ever reproduce.
   */
  const now = Date.now();

  return (
    <Shell personId={person.personId}>
      <FormsList key={chosen.id} events={events} chosen={chosen} forms={forms}
        now={now} canManage={mine.has("forms.manage")} />
    </Shell>
  );
}
