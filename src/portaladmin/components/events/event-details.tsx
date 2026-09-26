"use client";

import { useRef, useState } from "react";
import { Tick02Icon, Undo03Icon } from "@hugeicons/core-free-icons";
import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { Icon } from "@/components/ui/icon";
import type { AdminAnnouncement } from "@/app/events/announcements";
import { Announcements } from "./announcements";
import { ScheduleForm } from "./schedule";
import type { EventRow, Registration } from "./types";
import styles from "./events.module.css";

const tabs = ["Settings", "Announcements"] as const;

export function EventDetails({ event, dateLabel, registration, announcements, canManage, canPost }: {
  event: EventRow;
  dateLabel: string;
  registration: Registration;
  announcements: AdminAnnouncement[];
  canManage: boolean;
  canPost: boolean;
}) {
  const [activeTab, setActiveTab] = useState(0);
  const tabButtons = useRef<(HTMLButtonElement | null)[]>([]);
  const registrationLabel = { open: "Registration open", upcoming: "Registration not open yet", closed: "Registration closed", undecided: "Registration not decided yet" }[registration];

  return <div className={styles.page}>
    <ScheduleForm event={event} canManage={canManage} active={activeTab === 0}
      renderHeader={({ saving, saved, dirty, canSave, discard }) => <header className={styles.header}>
        <div className={styles.headerNavigation}>
          <Link href="/events" className={styles.backButton} aria-label="Back to events">
            <Icon icon={Undo03Icon} size={17} strokeWidth={2} />
            <span>Back</span>
          </Link>
          {canManage && activeTab === 0 ? <div className={styles.headerActions}>
            <span className={saved ? styles.saved : styles.saveHint} role="status">
              {saving ? "Saving changes…" : dirty ? "Unsaved changes" : saved ? <><Icon icon={Tick02Icon} size={16} />Saved</> : null}
            </span>
            {dirty ? <button type="button" className={styles.discardButton} onClick={discard} disabled={saving}>Discard</button> : null}
            <button type="submit" form="event-settings" className={`primary ${styles.saveButton}`} disabled={!canSave}>
              {saving ? "Saving…" : "Save changes"}
            </button>
          </div> : null}
        </div>
        <div className={styles.headerMain}>
          <div className={styles.identity}>
            <div className={styles.titleRow}>
              <h1>{event.name}</h1>
              <span className={styles.registrationBadge} data-state={registration}><span />{registrationLabel}</span>
            </div>
            <p>{dateLabel}</p>
          </div>
        </div>
        <div className={styles.tabs} role="tablist" aria-label="Event sections">
          {tabs.map((tab, index) => <button key={tab} type="button" role="tab" id={`event-tab-${index}`}
            ref={(element) => { tabButtons.current[index] = element; }}
            aria-selected={activeTab === index} aria-controls={`event-panel-${index}`} tabIndex={activeTab === index ? 0 : -1}
            onClick={() => setActiveTab(index)} onKeyDown={(event) => {
              if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
              event.preventDefault();
              const next = event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : (index + (event.key === "ArrowRight" ? 1 : -1) + tabs.length) % tabs.length;
              setActiveTab(next);
              tabButtons.current[next]?.focus();
            }}>{tab}{index === 1 && announcements.length > 0 ? <span>{announcements.length}</span> : null}</button>)}
        </div>
      </header>} />
    <div className={styles.panel} role="tabpanel" id="event-panel-1" aria-labelledby="event-tab-1" hidden={activeTab !== 1} tabIndex={0}>
      <Announcements eventId={event.id} announcements={announcements} canPost={canPost} />
    </div>
  </div>;
}
