"use client";

import { useId, useState } from "react";
import Link from "next/link";
import { MLH_BADGE_OPTIONS, formMlhBadgeColor, mlhBadgeImageUrl, type FormTheme, type MlhBadgeColor } from "../../../../../libs/ui/form-theme";
import styles from "./builder.module.css";

export function MlhSettings({ enabled, season, eventId, disabled, color = "white", onChange, onColorChange }: {
  enabled: boolean;
  season: number | null;
  eventId: string;
  disabled: boolean;
  color: FormTheme["mlhBadgeColor"];
  onChange: (enabled: boolean) => void;
  onColorChange: (color: MlhBadgeColor) => void;
}) {
  const id = useId();
  const selectedColor = formMlhBadgeColor({ mlhBadgeColor: color });
  const colorLabel = MLH_BADGE_OPTIONS.find(option => option.value === selectedColor)?.label ?? "Original";

  return <section className={styles.settingsPanel} data-setting="mlh">
    <label className={`${styles.settingsSummary} ${styles.badgeSetting}`}>
      <img className={styles.mlhLogo} src="https://static.mlh.io/brand-assets/logo/official/mlh-logo-color.svg" alt="MLH" width={36} height={16} />
      <span className={styles.settingsCopy}>
        <span>MLH badge</span>
        <strong>{season ? `${season} season · ${colorLabel}` : "Event date needed"}</strong>
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
      {enabled && season ? <fieldset className={styles.badgePicker} disabled={disabled}>
        <legend>Badge style</legend>
        <div className={styles.badgeOptions}>
          {MLH_BADGE_OPTIONS.map(option => <label key={option.value} className={styles.badgeOption}>
            <BadgePreview key={`${season}-${option.value}`} season={season} color={option.value} />
            <span className={styles.badgeOptionLabel}>
              <input type="radio" name={`${id}-color`} value={option.value} checked={selectedColor === option.value} onChange={() => onColorChange(option.value)} />
              <span>{option.label}</span>
            </span>
          </label>)}
        </div>
      </fieldset> : null}
      <div className={styles.badgeFooter}>
        <span>For official MLH member events</span>
        <a href="https://www.mlh.com/brand-guidelines" target="_blank" rel="noreferrer">Guidelines</a>
      </div>
    </div>
  </section>;
}

function BadgePreview({ season, color }: { season: number; color: MlhBadgeColor }) {
  const [unavailable, setUnavailable] = useState(false);
  return <span className={styles.badgeArt} aria-hidden="true">
    {!unavailable ? <img src={mlhBadgeImageUrl(season, color)} alt="" width={36} height={64} onError={() => setUnavailable(true)} /> : null}
  </span>;
}
