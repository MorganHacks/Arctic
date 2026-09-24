import { Mail01Icon, Link01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { EmptyState } from "@/components/ui/empty-state";
import { apiFetch } from "@/lib/api";
import { numbers, percentage } from "./home-analytics";
import { categoryColor } from "./home-palette";
import { HomeUpdatedAt } from "./home-updated-at";
import { BestEmailsCard, type EmailCampaignPerformance } from "./home-best-emails";
import styles from "./home-overview.module.css";

type EmailAnalytics = {
  sentEmails: number;
  trackedEmails: number;
  clickedEmails: number;
  totalClicks: number;
  clickedRecipients: number;
  trackingEnabledTemplates: number;
  templates: { name: string; clicks: number; clickedEmails: number }[];
};

export function HomeEmailView({ data, error }: { data?: EmailAnalytics; error?: boolean }) {
  return <section className={styles.emailSection} aria-labelledby="email-performance-title">
    <div className={styles.sectionHeading}>
      <div><h2 id="email-performance-title">Email performance</h2><p>All-time · across the workspace</p></div>
      <Icon icon={Mail01Icon} size={19} className={styles.sectionIcon} />
    </div>
    {error ? <p className={styles.dataNote}>Email analytics couldn’t load. Try refreshing the dashboard.</p>
      : <div className={styles.emailGrid}>
        <div>
          <div className={styles.emailStats}>
            <div><span>Link clicks</span><strong>{data ? numbers.format(data.totalClicks) : "—"}</strong>
              <small>Includes repeat clicks</small></div>
            <div><span>Recipients who clicked</span><strong>{data ? numbers.format(data.clickedRecipients) : "—"}</strong>
              <small>Unique email addresses</small></div>
            <div><span>Click rate</span><strong>{data?.trackedEmails ? percentage(data.clickedEmails, data.trackedEmails) : "—"}</strong>
              <small>Tracked emails with a click</small></div>
          </div>
          <p className={styles.emailSummary}>{data
            ? `${numbers.format(data.sentEmails)} emails sent · ${numbers.format(data.trackedEmails)} with tracked links`
            : "Loading email activity…"}</p>
          <p className={styles.dataNote}>{data
            ? `Click tracking is enabled on ${numbers.format(data.trackingEnabledTemplates)} ${data.trackingEnabledTemplates === 1 ? "template" : "templates"}. Test emails are excluded.`
            : "Your tracked email results will appear here."}</p>
        </div>
        <div className={styles.emailTemplates}>
          <h3>Most clicked templates</h3>
          {data?.templates.length ? <ol className={styles.bars}>
            {data.templates.map((template, index) => <li key={`${template.name}-${index}`}>
              <div className={styles.barHeading}><span>{template.name}</span><strong>{numbers.format(template.clicks)}<small>clicks</small></strong></div>
              <div className={styles.barTrack} aria-hidden="true"><span style={{ width: `${template.clicks / data.totalClicks * 100}%`, backgroundColor: categoryColor(template.name) }} /></div>
            </li>)}
          </ol> : data ? <EmptyState title="No clicks yet"
            description="Clicks will appear when recipients follow links in an email with tracking enabled." /> : <div className={styles.emailEmpty}>
            <Icon icon={Link01Icon} size={23} />
            <p>Loading click activity…</p>
          </div>}
        </div>
      </div>}
    {data && data.totalClicks > 0 ? <p className={styles.dataNote}>Click counts can include automated email security checks.</p> : null}
  </section>;
}

export async function HomeEmail() {
  try {
    const response = await apiFetch("/admin/analytics/email", { signal: AbortSignal.timeout(10000) });
    if (!response.ok) return <HomeEmailView error />;
    const data = await response.json() as EmailAnalytics;
    return <><HomeEmailView data={data} /><HomeUpdatedAt at={new Date().toISOString()} /></>;
  } catch {
    return <HomeEmailView error />;
  }
}

export async function HomeBestEmails() {
  try {
    const response = await apiFetch("/admin/analytics/email/campaigns", { signal: AbortSignal.timeout(10000) });
    if (!response.ok) return <BestEmailsCard error />;
    const data = await response.json() as { campaigns: EmailCampaignPerformance[] };
    return <BestEmailsCard campaigns={data.campaigns} />;
  } catch {
    return <BestEmailsCard error />;
  }
}
