import { EmptyState } from "@/components/ui/empty-state";

/**
 * Nothing has been mailed yet.
 *
 * The one thing worth saying is what creating a campaign does, because the
 * panel above this is where it is created and the useful thing to know before
 * pressing the button is that it does not send anything.
 */
export function NoCampaigns() {
  return (
    <EmptyState size="page" title="No campaigns yet"
      description="A new campaign is a draft. Nothing goes out until its recipients have been previewed." />
  );
}
