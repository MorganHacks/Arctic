"use client";

import { Select } from "@/components/ui/select";
import { useRef, useState, useTransition, type KeyboardEvent, type ReactNode } from "react";
import {
  ArrowRight01Icon, Calendar03Icon, ChartHistogramIcon, Clock01Icon,
  DocumentValidationIcon, Mail01Icon, SchoolIcon, UserGroupIcon,
} from "@hugeicons/core-free-icons";
import { Icon, type IconSvgElement } from "@/components/ui/icon";
import { EmptyState } from "@/components/ui/empty-state";
import { SchoolLogo } from "@/components/ui/school-logo";
import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { loadApplicantAnalytics } from "./home-actions";
import { ResponseActivity } from "./home-activity";
import { HomeUpdatedAt } from "./home-updated-at";
import { analyticsColors, applicationColors, categoryColor, type AnalyticsTone } from "./home-palette";
import {
  applicantsUrl, numbers, percentage, statusLabels,
  type SchoolBucket, type AnalyticsResult, type ApplicantAnalytics,
} from "./home-analytics";
import styles from "./home-overview.module.css";

const applicantTabs = [
  { id: "overview", label: "Overview", icon: ChartHistogramIcon },
  { id: "applications", label: "Applications", icon: DocumentValidationIcon },
  { id: "demographics", label: "Demographics", icon: UserGroupIcon },
] as const;

function Stat({ label, value, note, tone }: { label: string; value: number | null; note: string; tone: AnalyticsTone }) {
  return <section className={styles.stat} data-tone={tone}>
    <p>{label}</p>
    <strong>{value === null ? "—" : numbers.format(value)}</strong>
    <span>{note}</span>
  </section>;
}

function Breakdown({ title, subtitle, items, total, empty, icon, loading, limit = 6, schools = false, colors, onViewAll }: {
  title: string; subtitle: string; items: SchoolBucket[]; total: number; empty: string;
  icon: IconSvgElement; loading: boolean; limit?: number; schools?: boolean; colors?: Record<string, string>; onViewAll?: () => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const shown = expanded ? items : items.slice(0, limit);
  return <section className={styles.breakdown} aria-label={title}>
    <div className={styles.sectionHeading}>
      <div><h2>{title}</h2><p>{subtitle}</p></div>
      <Icon icon={icon} size={19} className={styles.sectionIcon} />
    </div>
    {total > 0 && items.length > 0 ? <ol className={styles.bars}>
      {shown.map((item) => <li key={item.label} className={schools ? styles.schoolRow : undefined}>
        <div className={styles.barHeading}><span className={schools ? styles.schoolName : undefined}>
          {schools ? <SchoolLogo src={item.logoUrl} /> : null}<span>{item.label}</span></span>
          <strong>{numbers.format(item.count)}<small>{percentage(item.count, total)}</small></strong></div>
        <div className={styles.barTrack} aria-hidden="true"><span className={item.label === "Not provided" ? styles.unknown : ""}
          style={{ width: `${Math.min(100, item.count / total * 100)}%`, backgroundColor: colors?.[item.label] ?? categoryColor(item.label) }} /></div>
      </li>)}
    </ol> : loading ? <div className={styles.loadingEmpty}><p>{empty}</p></div>
      : <EmptyState className={styles.breakdownEmpty} title={schools ? "No schools yet" : "No data to display"} description={empty} />}
    {items.length > limit ? <button type="button" className={styles.textButton} onClick={onViewAll ?? (() => setExpanded(!expanded))}>
      {onViewAll ? <>View all schools<Icon icon={ArrowRight01Icon} size={15} /></> : expanded ? "Show less" : `Show all ${numbers.format(items.length)}`}
    </button> : null}
  </section>;
}

function Attention({ analytics, eventId, loading }: { analytics: ApplicantAnalytics | null; eventId?: string; loading: boolean }) {
  const statuses = analytics?.statuses ?? {};
  const items = [
    { title: "Awaiting review", note: "Submitted and under review", statuses: ["submitted", "under_review"], icon: DocumentValidationIcon },
    { title: "Unfinished applications", note: "Started, not submitted", statuses: ["incomplete"], icon: Clock01Icon },
    { title: "Awaiting RSVP", note: "Accepted, not yet confirmed", statuses: ["accepted"], icon: UserGroupIcon },
    { title: "On the waitlist", note: "Waiting for a place", statuses: ["waitlisted"], icon: Calendar03Icon },
  ].map((item) => ({ ...item, count: item.statuses.reduce((sum, status) => sum + (statuses[status] ?? 0), 0) }))
    .filter((item) => item.count > 0);

  return <section className={styles.attention} aria-labelledby="attention-title">
    <div className={styles.sectionHeading}><h2 id="attention-title">Needs attention</h2>
      <span className={styles.badge}>{loading ? "—" : numbers.format(items.reduce((sum, item) => sum + item.count, 0))}</span>
    </div>
    {items.length > 0 ? <ul className={styles.attentionList}>{items.map((item) => <li key={item.title}>
      <Link href={applicantsUrl(eventId, item.statuses)}>
        <span className={styles.attentionIcon}><Icon icon={item.icon} size={19} /></span>
        <span><strong>{item.title}</strong><small>{item.note}</small></span>
        <b>{numbers.format(item.count)}</b><Icon icon={ArrowRight01Icon} size={15} />
      </Link>
    </li>)}</ul> : loading ? <div className={styles.loadingEmpty}><p>Loading applications…</p></div>
      : <EmptyState className={styles.attentionEmpty} title={!eventId ? "A clear starting point" : "You’re all caught up"}
        description={!eventId
        ? "Once applications arrive, we’ll show you what needs a closer look."
        : "No applications need your attention right now."} />}
  </section>;
}

export function HomeOverview({ initial, email, bestEmails, greeting }: { initial?: AnalyticsResult; email?: ReactNode; bestEmails?: ReactNode; greeting: ReactNode }) {
  const [result, setResult] = useState(initial);
  const [error, setError] = useState<string | null>(null);
  const [refreshing, startTransition] = useTransition();
  const [tab, setTab] = useState<"overview" | "applications" | "demographics" | "email">("overview");
  const tabs = [...applicantTabs, ...(email ? [{ id: "email" as const, label: "Email", icon: Mail01Icon }] : [])];
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  const data = result?.data;
  const analytics = data?.analytics ?? null;
  const loading = !result;
  const failed = result?.error;
  const count = (status: string) => analytics?.statuses[status] ?? 0;
  const total = analytics?.totalApplicants ?? 0;
  const submitted = analytics?.submittedApplications ?? 0;
  const schools = analytics?.schools.filter((school) => school.label !== "Not provided").length ?? 0;
  const demographics = analytics?.demographics;
  const gender = demographics?.genderCollected
    ? ["Men", "Women", "Non-binary", "Self-described", "Prefer not to answer", "Not provided"]
      .map((label) => ({ label, count: demographics.gender.find((item) => item.label === label)?.count ?? 0 }))
      .filter((item) => item.count > 0 || item.label === "Men" || item.label === "Women")
    : [];
  const breakdownEmpty = loading ? "Loading responses…" : !data?.chosen
    ? "Applicant details will appear once your event receives applications." : "No submitted applications yet.";
  const demographicEmpty = data && !data.canViewResponses
    ? "Access to application responses is needed to see this breakdown." : breakdownEmpty;

  function load(eventId?: string) {
    startTransition(async () => {
      setError(null);
      const next = await loadApplicantAnalytics(eventId);
      if (next.error && data) setError(next.error);
      else setResult(next);
    });
  }

  function move(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const next = event.key === "ArrowRight" ? (index + 1) % tabs.length
      : event.key === "ArrowLeft" ? (index + tabs.length - 1) % tabs.length
      : event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : null;
    if (next === null) return;
    event.preventDefault();
    setTab(tabs[next].id);
    buttons.current[next]?.focus();
  }

  return <section className={styles.overview} aria-label="Applicant analytics" aria-busy={loading || refreshing}>
    <div className={styles.intro}>
      {greeting}
      {tab !== "email" ? <div className={styles.eventControl}>
        <label htmlFor="analytics-event">Event</label>
        <div><Icon icon={Calendar03Icon} size={16} />
          <Select id="analytics-event" value={data?.chosen?.id ?? ""} disabled={!data?.events.length || refreshing}
            onChange={(event) => load(event.target.value)}>
            {data?.events.length ? data.events.map((event) => <option key={event.id} value={event.id}>{event.name}</option>)
              : <option value="">{loading ? "Loading events…" : failed ? "Events unavailable" : "No events yet"}</option>}
          </Select>
        </div>
      </div> : null}
    </div>
    <>
      <div className={styles.tabs} role="tablist" aria-label="Dashboard sections">
        {tabs.map((item, index) => <button key={item.id} ref={(element) => { buttons.current[index] = element; }}
          type="button" role="tab" id={`dashboard-tab-${item.id}`} aria-selected={tab === item.id}
          aria-controls={`dashboard-panel-${item.id}`} tabIndex={tab === item.id ? 0 : -1}
          onClick={() => setTab(item.id)} onKeyDown={(event) => move(event, index)}>
          <Icon icon={item.icon} size={15} />{item.label}
        </button>)}
        <span className={styles.scopeNote}>{refreshing ? "Updating…" : tab === "email" ? "All-time · across the workspace" : "All-time totals · selected event"}</span>
      </div>
      {error ? <p className={styles.inlineError} role="alert">{error}</p> : null}
      {!failed && tab !== "email" ? <div className={styles.stats}>
        <Stat label="Total applicants" value={loading ? null : total} tone="sky"
          note={loading ? "Loading totals…" : `${numbers.format(count("incomplete"))} with unfinished applications`} />
        <Stat label="Responses received" value={loading ? null : submitted} tone="violet"
          note={loading ? "Loading responses…" : total > 0 ? `${percentage(submitted, total)} of applicants submitted` : "No submissions yet"} />
        <Stat label="Awaiting review" value={loading ? null : count("submitted") + count("under_review")} tone="amber"
          note={loading ? "Loading review queue…" : `${numbers.format(count("confirmed") + count("checked_in"))} confirmed to attend`} />
      </div> : null}
      {tabs.map((item) => <div key={item.id} role="tabpanel" hidden={tab !== item.id}
        id={`dashboard-panel-${item.id}`} aria-labelledby={`dashboard-tab-${item.id}`}
        className={styles.panel} tabIndex={0}>
        {tab === item.id ? failed && tab !== "email" ? <div className={styles.error} role="alert"><p>{failed}</p>
          <button type="button" onClick={() => load()} disabled={refreshing}>{refreshing ? "Trying again…" : "Try again"}</button>
        </div> : <>
          {tab === "overview" ? <><div className={styles.overviewGrid}>
            <ResponseActivity key={data?.chosen?.id ?? "empty"} activity={analytics?.activity ?? []}
              eventId={data?.chosen?.id} loading={loading} />
            <Breakdown loading={loading} title="Top schools" subtitle={`${numbers.format(schools)} schools · submitted applications`}
              items={analytics?.schools.filter((school) => school.label !== "Not provided") ?? []}
              total={submitted} empty={breakdownEmpty} icon={SchoolIcon} schools limit={5}
              onViewAll={() => { setTab("demographics"); buttons.current[2]?.focus(); }} />
          </div>{bestEmails}</> : null}
          {tab === "applications" ? <div className={styles.detailPanel}>
            <Attention analytics={analytics} eventId={data?.chosen?.id} loading={loading} />
            <Breakdown loading={loading} title="Application status" subtitle="Every applicant in the selected event"
              items={Object.entries(statusLabels).map(([status, label]) => ({ label, count: count(status) })).filter((item) => item.count > 0)}
              total={total} empty={loading ? "Loading applications…" : "No applications to break down yet."}
              icon={ChartHistogramIcon} limit={12} colors={applicationColors} />
          </div> : null}
          {tab === "demographics" ? <div className={styles.detailPanel}>
            <Breakdown loading={loading} key={`schools-${data?.chosen?.id}`} title="Where applicants study"
              subtitle={`${numbers.format(schools)} schools represented · submitted applications`}
              items={analytics?.schools ?? []} total={submitted} empty={breakdownEmpty} icon={SchoolIcon} schools />
            <Breakdown loading={loading} title="Gender" subtitle="Self-reported · submitted applications" items={gender} total={submitted}
              colors={{ Men: analyticsColors.violet, Women: analyticsColors.teal, "Non-binary": analyticsColors.amber,
                "Self-described": analyticsColors.sky, "Prefer not to answer": analyticsColors.rose }}
              empty={data?.canViewResponses && analytics && !demographics?.genderCollected
                ? "Gender isn’t collected on this application form yet." : demographicEmpty} icon={UserGroupIcon} />
            <Breakdown loading={loading} title="Level of study" subtitle="Submitted applications" items={demographics?.education ?? []}
              total={submitted} empty={demographicEmpty} icon={SchoolIcon} />
            <Breakdown loading={loading} title="Hackathon experience" subtitle="Submitted applications" items={demographics?.experience ?? []}
              total={submitted} empty={demographicEmpty} icon={UserGroupIcon} />
          </div> : null}
          {tab === "email" ? email : null}
        </> : null}
      </div>)}
      {!failed && tab === "demographics" ? <p className={styles.dataNote}>Percentages use submitted applications. Unanswered questions appear as “Not provided”.</p> : null}
      {tab !== "email" && result?.updatedAt ? <HomeUpdatedAt at={result.updatedAt} /> : null}
    </>
  </section>;
}
