/**
 * An event with no forms on it.
 *
 * Not a failure and not empty furniture. The one thing worth saying is what
 * happens when the first form is made, because the panel above this is where it
 * gets made and the useful thing to know before pressing Create is that the
 * application form is not started from nothing.
 */
export function NoForms({ event }: { event: string }) {
  return (
    <div className="empty">
      No forms on {event} yet. An application form starts with a standard set of
      questions already on it.
    </div>
  );
}
