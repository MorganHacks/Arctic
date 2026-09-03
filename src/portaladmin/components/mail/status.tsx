import type { CampaignStatus } from "./types";

/**
 * Where a campaign has got to.
 *
 * Coloured because it changes what can be done next and nothing else: sent is
 * the state that cannot be taken back, failed is the one somebody has to act
 * on, and a draft sitting there is neither. The two quiet states share the
 * neutral pill on purpose — colouring all six would leave nothing standing out
 * on a list of thirty.
 */
const PILLS: Record<CampaignStatus, { className: string; label: string }> = {
  draft: { className: "pill lapsed", label: "Draft" },
  queued: { className: "pill expiring", label: "Queued" },
  sending: { className: "pill expiring", label: "Sending" },
  sent: { className: "pill active", label: "Sent" },
  cancelled: { className: "pill lapsed", label: "Cancelled" },
  failed: { className: "pill revoked", label: "Failed" },
};

export function StatusPill({ status }: { status: CampaignStatus }) {
  // A status the API started using that this screen has not met yet. Shown as
  // itself rather than dropped, because an unlabelled state is still a state
  // somebody needs to see.
  const pill = PILLS[status] ?? { className: "pill lapsed", label: status };

  return <span className={pill.className}>{pill.label}</span>;
}
