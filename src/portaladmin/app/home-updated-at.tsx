"use client";

import { useEffect, useState } from "react";
import styles from "./home-overview.module.css";

export function HomeUpdatedAt({ at }: { at: string }) {
  const [ready, setReady] = useState(false);

  useEffect(() => setReady(true), []);

  const time = ready ? new Intl.DateTimeFormat("en-US", {
    hour: "numeric", minute: "2-digit", hour12: true,
  }).format(new Date(at)) : "\u00a0";

  return <footer className={styles.updatedAt}>Last updated <time dateTime={at}>{time}</time></footer>;
}
