"use client";

import { useId } from "react";
import Link from "next/link";
import styles from "./builder.module.css";

export function MlhSettings({ enabled, season, eventId, disabled, onChange }: {
  enabled: boolean;
  season: number | null;
  eventId: string;
  disabled: boolean;
  onChange: (enabled: boolean) => void;
}) {
  const id = useId();

  return <section className={styles.settingsPanel} data-setting="mlh">
    <label className={`${styles.settingsSummary} ${styles.badgeSetting}`}>
      <img className={styles.mlhLogo} src="https://static.mlh.io/brand-assets/logo/official/mlh-logo-color.svg" alt="MLH" width={36} height={16} />
      <span className={styles.settingsCopy}>
        <span>MLH badge</span>
        <strong>{season ? `${season} season · Automatic` : "Event date needed"}</strong>
      </span>
      <input type="checkbox" role="switch" className={styles.requiredSwitch} aria-label="Show MLH badge"
        aria-describedby={id} checked={enabled} disabled={disabled}
        onChange={event => onChange(event.target.checked)} />
    </label>
    <div className={styles.badgeDetails}>
      <p id={id}>Shown on the live form. Hidden while editing or previewing.</p>
      {!season ? <div className={styles.badgeDateNotice}>
        <span>Add your event date to select the season automatically.</span>
        <Link href={`/events/${eventId}`}>Set date</Link>
      </div> : <p className={styles.badgeSeasonNote}>Selected from your event’s start date.</p>}
      <div className={styles.badgeFooter}>
        <span>For official MLH member events</span>
        <a href="https://www.mlh.com/brand-guidelines" target="_blank" rel="noreferrer">Guidelines</a>
      </div>
    </div>
  </section>;
}
