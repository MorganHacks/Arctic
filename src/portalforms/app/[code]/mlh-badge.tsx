"use client";

import { useState } from "react";
import { mlhBadgeImageUrl, type MlhBadgeColor } from "../../../../libs/ui/form-theme";
import styles from "./mlh-badge.module.css";

export function MlhBadge({ season, color = "white" }: { season: number; color?: MlhBadgeColor }) {
  const variant = `${season}-${color}`;
  const [unavailable, setUnavailable] = useState<string | null>(null);
  if (unavailable === variant) return null;

  return <a id="mlh-trust-badge" className={styles.badge}
    href={`https://mlh.io/na?utm_source=na-hackathon&utm_medium=TrustBadge&utm_campaign=${season}-season&utm_content=${color}`}
    target="_blank" rel="noreferrer">
    <img key={variant} src={mlhBadgeImageUrl(season, color)}
      alt={`Major League Hacking ${season} Hackathon Season`}
      width={100} height={177} onError={() => setUnavailable(variant)} />
  </a>;
}
