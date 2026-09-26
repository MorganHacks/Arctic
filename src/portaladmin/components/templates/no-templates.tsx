import { EmptyState } from "@/components/ui/empty-state";

/**
 * There are no templates.
 *
 * The state this system is in today, and the reason no mass email has ever
 * been sent from it. The one thing worth saying is what a template is for,
 * because the button above this is where the first one gets written — and not
 * a word of what one should say, which is not this screen's to suggest.
 */
export function NoTemplates() {
  return (
    <EmptyState variant="canvas" size="page" title="No templates yet"
      description="Create a reusable email for campaigns and automated messages." />
  );
}
