"use client";

import Link, { useLinkStatus } from "next/link";
import { useState, type ComponentProps } from "react";
import { createPortal } from "react-dom";
import styles from "./navigation-link.module.css";

function NavigationProgress() {
  const { pending } = useLinkStatus();

  return pending ? createPortal(
    <div className={styles.progress} role="status">
      <span className={styles.label}>Loading page…</span>
      <span className={styles.bar} aria-hidden="true" />
    </div>,
    document.body,
  ) : null;
}

export function NavigationLink({ children, prefetch, onMouseEnter, onFocus, ...props }: ComponentProps<typeof Link>) {
  const [intent, setIntent] = useState(false);

  return <Link {...props} prefetch={prefetch === undefined ? intent : prefetch}
    onMouseEnter={(event) => { onMouseEnter?.(event); if (!event.defaultPrevented) setIntent(true); }}
    onFocus={(event) => { onFocus?.(event); if (!event.defaultPrevented) setIntent(true); }}>
    {children}<NavigationProgress />
  </Link>;
}
