"use client";

import { useEffect, useState } from "react";
import styles from "./home-greeting.module.css";

const pixels = [
  "001001010011000010100001",
  "000100110100100001000000",
  "001001001011010000010010",
  "010011010100101001000000",
  "100100111010010010100100",
  "010010100101101000010000",
  "001000010010010100000000",
];

export function HomeGreeting({ name }: { name: string }) {
  const [now, setNow] = useState<Date | null>(null);

  useEffect(() => {
    const update = () => setNow(new Date());
    update();
    const timer = window.setInterval(update, 60_000);
    window.addEventListener("focus", update);
    return () => {
      window.clearInterval(timer);
      window.removeEventListener("focus", update);
    };
  }, []);

  const firstName = name.trim().split(/\s+/)[0];
  const hour = now?.getHours();
  const greeting = hour === undefined ? "Welcome back" : hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
  const date = now ? new Intl.DateTimeFormat("en-US", { weekday: "long", month: "long", day: "numeric" }).format(now) : "\u00a0";
  const localDate = now ? `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}` : undefined;

  return <div className={styles.greeting}>
    <div className={styles.pattern} aria-hidden="true">
      {pixels.flatMap((row, y) => [...row].map((pixel, x) => pixel === "1"
        ? <span key={`${x}-${y}`} style={{ gridColumn: x + 1, gridRow: y + 1 }} /> : null))}
    </div>
    <time className={styles.date} dateTime={localDate}>{date}</time>
    <h1 className={styles.title}>{greeting}{firstName ? `, ${firstName}` : ""}.</h1>
  </div>;
}
