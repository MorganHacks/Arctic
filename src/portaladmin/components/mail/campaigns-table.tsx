import Link from "next/link";
import styles from "./mail.module.css";
import { StatusPill } from "./status";
import { when, type CampaignRow } from "./types";

/**
 * Every campaign, newest first.
 *
 * Four columns, because they are the four questions somebody opens this screen
 * to answer: what was it called, where has it got to, how many people did it
 * reach, and when did it go.
 *
 * The recipient count is blank on a draft rather than nought. A draft has not
 * resolved its segment yet, and a column of zeroes reads as "this campaign
 * reaches nobody" instead of "nobody has asked yet".
 */
export function CampaignsTable({ campaigns }: { campaigns: CampaignRow[] }) {
  return (
    <table>
      <thead>
        <tr>
          <th>Campaign</th>
          <th>Status</th>
          <th>Recipients</th>
          <th>Sent</th>
        </tr>
      </thead>
      <tbody>
        {campaigns.map((campaign) => (
          <tr key={campaign.id}>
            <td>
              <Link href={`/mail/${campaign.id}`}>{campaign.name}</Link>
              <div className="meta">Created {when(campaign.createdAt)}</div>
            </td>

            <td>
              <StatusPill status={campaign.status} />
            </td>

            <td className={styles.numeric}>
              {campaign.status === "draft" ? "—" : campaign.recipientCount}
            </td>

            <td className={styles.numeric}>{when(campaign.sentAt)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
