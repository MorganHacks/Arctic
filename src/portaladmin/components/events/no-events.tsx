import { EmptyState } from "@/components/ui/empty-state";

export function NoEvents({ canManage }: { canManage: boolean }) {
  return (
    <EmptyState variant="canvas" size="page" title="Your next event starts here"
      description={canManage ? "Choose New event to create a home for your forms, applicants and mail." : "Your events will appear here once an organizer creates one."} />
  );
}
