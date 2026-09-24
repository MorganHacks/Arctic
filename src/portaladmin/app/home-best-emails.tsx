"use client";

import { useState } from "react";
import { ChartHistogramIcon, Mail01Icon, MinusSignIcon, PlusSignIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { EmptyState } from "@/components/ui/empty-state";
import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { emailDocument } from "@/components/templates/email-preview";
import { numbers, percentage } from "./home-analytics";
import styles from "./home-best-emails.module.css";

export type EmailCampaignPerformance = {
  id: string;
  name: string;
  sentAt: string;
  sentEmails: number;
  trackedEmails: number;
  clickedEmails: number;
  totalClicks: number;
  previewHtml: string | null;
};

export function BestEmailsCard({ campaigns, error = false }: {
  campaigns?: EmailCampaignPerformance[]; error?: boolean;
}) {
  const [expanded, setExpanded] = useState(true);

  return <section className={styles.card} aria-labelledby="best-emails-title" aria-busy={!campaigns && !error}>
    <header className={styles.header}>
      <div className={styles.heading}><h2 id="best-emails-title">Best performing emails</h2>
        <span>All time · ranked by click rate</span></div>
      <div className={styles.actions}>
        <Link href="/mail"><Icon icon={Mail01Icon} size={15} />See all emails</Link>
        <button type="button" aria-label={expanded ? "Collapse best performing emails" : "Expand best performing emails"}
          aria-expanded={expanded} aria-controls="best-emails-list" onClick={() => setExpanded(!expanded)}>
          <Icon icon={expanded ? MinusSignIcon : PlusSignIcon} size={15} />
        </button>
      </div>
    </header>
    <div id="best-emails-list" hidden={!expanded}>
      {error ? <div className={styles.empty} role="status"><Icon icon={Mail01Icon} size={24} />
        <div><strong>Email performance couldn’t load</strong><p>Refresh the dashboard to try again.</p></div></div>
      : !campaigns ? <div className={styles.empty} role="status"><Icon icon={Mail01Icon} size={24} /><p>Loading email performance…</p></div>
      : campaigns.length === 0 ? <EmptyState className={styles.noCampaigns} title="Your next send starts here"
        description="Sent campaigns and their click results will appear here." />
      : <ol className={styles.rows} aria-label="Best performing email campaigns">
        {campaigns.map((campaign) => <li key={campaign.id} className={styles.row}>
          <Link className={styles.campaign} href={`/mail/${campaign.id}`}>
            <span className={styles.preview} aria-hidden="true">
              {campaign.previewHtml ? <span className={styles.previewDocument}><iframe title={`${campaign.name} preview`} tabIndex={-1}
                loading="lazy" sandbox="" referrerPolicy="no-referrer"
                srcDoc={emailDocument(campaign.previewHtml, "desktop")} /></span> : <Icon icon={Mail01Icon} size={22} />}
            </span>
            <span className={styles.campaignCopy}><strong>{campaign.name}</strong>
              <span className={styles.sent}><span className={styles.sentState}>Sent</span><time dateTime={campaign.sentAt}>{new Intl.DateTimeFormat("en-US", {
                month: "short", day: "numeric", year: "numeric", timeZone: "UTC",
              }).format(new Date(campaign.sentAt))}</time></span>
            </span>
          </Link>
          <dl className={styles.metrics}>
            <div><dt>sent</dt><dd>{numbers.format(campaign.sentEmails)}</dd></div>
            <div title={`${numbers.format(campaign.clickedEmails)} of ${numbers.format(campaign.trackedEmails)} tracked emails received a click`}>
              <dt>click rate</dt><dd>{campaign.trackedEmails ? percentage(campaign.clickedEmails, campaign.trackedEmails) : "—"}</dd></div>
            <div><dt>total clicks</dt><dd>{numbers.format(campaign.totalClicks)}</dd></div>
          </dl>
          <Link className={styles.report} href={`/mail/${campaign.id}`} aria-label={`View ${campaign.name} report`}>
            <Icon icon={ChartHistogramIcon} size={18} />
          </Link>
        </li>)}
      </ol>}
    </div>
  </section>;
}
