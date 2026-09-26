"use client";

import { useEffect, useState } from "react";
import { COUNTDOWN_WINDOW_MS, countdownParts, deadlineSeconds } from "./deadline-time";
import styles from "./form-page.module.css";

export function DeadlineCountdown({ closesAt }: { closesAt: string }) {
  const [seconds, setSeconds] = useState<number | null>(null);

  useEffect(() => {
    const deadline = Date.parse(closesAt);
    if (!Number.isFinite(deadline)) return;

    let timeout: number;
    function update() {
      window.clearTimeout(timeout);
      const now = Date.now();
      const remaining = deadlineSeconds(closesAt, now);
      setSeconds(remaining);
      if (remaining === 0) return;

      const delay = remaining === null
        ? Math.min(60_000, Math.max(1, deadline - now - COUNTDOWN_WINDOW_MS))
        : 1000;
      timeout = window.setTimeout(update, delay);
    }

    update();
    document.addEventListener("visibilitychange", update);
    return () => {
      window.clearTimeout(timeout);
      document.removeEventListener("visibilitychange", update);
    };
  }, [closesAt]);

  if (seconds === null) return null;
  if (seconds === 0) return <p className={styles.deadlineClosed} role="status">The submission deadline has passed.</p>;

  const parts = countdownParts(seconds);
  return <div className={styles.countdown} role="timer" aria-live="off" aria-label="Time left to submit">
    <p>Time left to submit</p>
    <div className={styles.countdownDigits}>
      {parts.map((value, index) => <div className={styles.countdownUnit} key={index}>
        <strong>{value}</strong>
        <span>{["Hours", "Minutes", "Seconds"][index]}</span>
      </div>)}
    </div>
  </div>;
}
