"use client";

import { useState } from "react";
import { profileColor, profileInitials } from "@/lib/person-profile";
import styles from "./avatar.module.css";

export function Avatar({ name, email, avatarUrl, className }: {
  name: string;
  email?: string | null;
  avatarUrl?: string | null;
  className?: string;
}) {
  const [failedUrl, setFailedUrl] = useState<string | null>(null);
  const photoUrl = email?.trim().toLowerCase() === "imarooclinton@gmail.com"
    ? "https://avatars.githubusercontent.com/u/93707197?v=4"
    : avatarUrl;
  const source = photoUrl?.startsWith("https://") && photoUrl !== failedUrl
    ? photoUrl
    : null;

  return (
    <span
      className={`${styles.avatar} ${className ?? ""}`}
      style={{ backgroundColor: profileColor(email || name) }}
      aria-hidden="true"
    >
      {source ? (
        <img
          className={styles.image}
          src={source}
          alt=""
          referrerPolicy="no-referrer"
          onError={() => setFailedUrl(source)}
        />
      ) : profileInitials(name)}
    </span>
  );
}
