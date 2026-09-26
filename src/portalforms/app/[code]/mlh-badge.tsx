"use client";

import { useState } from "react";
import styles from "./mlh-badge.module.css";

export function MlhBadge({ season }: { season: number }) {
  const [unavailable, setUnavailable] = useState<number | null>(null);
  if (unavailable === season) return null;

  return <a id="mlh-trust-badge" className={styles.badge}
    href={`https://mlh.io/na?utm_source=na-hackathon&utm_medium=TrustBadge&utm_campaign=${season}-season&utm_content=white`}
    target="_blank" rel="noreferrer">
    <img src={`https://logged-assets.s3.amazonaws.com/trust-badge/${season}/mlh-trust-badge-${season}-white.svg`}
      alt={`Major League Hacking ${season} Hackathon Season`}
      width={100} height={177} onError={() => setUnavailable(season)} />
  </a>;
}
