import { EmptyState } from "@/components/ui/empty-state";

/**
 * An event with no forms on it.
 *
 * Not a failure and not empty furniture. The one thing worth saying is what
 * happens when the first form is made, because the New form dialog is where it
 * gets made and the useful thing to know before pressing Create is that the
 * application form is not started from nothing.
 */
export function NoForms({ event }: { event: string }) {
  return (
    <EmptyState size="page" title="No forms yet"
      description={`No forms on ${event} yet. An application form starts with a standard set of questions already on it.`} />
  );
}
