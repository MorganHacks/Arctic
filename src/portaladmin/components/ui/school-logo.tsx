"use client";

import { useState } from "react";
import { SchoolIcon } from "@hugeicons/core-free-icons";
import { Icon } from "./icon";
import styles from "./school-logo.module.css";

export function SchoolLogo({ src }: { src?: string }) {
  const [failed, setFailed] = useState<string>();
  return <span className={styles.logo} aria-hidden="true">
    {src && failed !== src ? <img src={src} alt="" width={32} height={32} loading="lazy" decoding="async"
      referrerPolicy="origin" onError={() => setFailed(src)} /> : <Icon icon={SchoolIcon} size={20} />}
  </span>;
}
