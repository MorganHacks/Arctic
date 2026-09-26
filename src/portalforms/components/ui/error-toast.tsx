"use client";

import { useEffect, useId, useRef, useSyncExternalStore } from "react";
import { createPortal } from "react-dom";
import { errorNotifications, type ErrorNotification } from "../../../../libs/ui/error-notifications";
import styles from "../../../../libs/ui/error-toast.module.css";

export function ErrorToast({ message, title = "Something needs attention", revision = 0, descriptionId }: {
  message?: string | null;
  title?: string;
  revision?: unknown;
  descriptionId?: string;
}) {
  const id = useId();
  const anchor = useRef<HTMLSpanElement>(null);
  useEffect(() => {
    if (!message) return;
    errorNotifications.show({ id, title, message, target: anchor.current?.closest("dialog") ?? null });
    return () => errorNotifications.dismiss(id);
  }, [id, message, title, revision]);
  return <span ref={anchor} id={descriptionId} hidden={!descriptionId} className={descriptionId ? styles.srOnly : undefined}>{descriptionId ? message : null}</span>;
}

export function ErrorDescription({ id, children }: { id?: string; children: React.ReactNode }) {
  return <span id={id} className={styles.srOnly}>{children}</span>;
}

export function ErrorToasts() {
  const notifications = useSyncExternalStore(errorNotifications.subscribe, errorNotifications.snapshot, errorNotifications.serverSnapshot);
  const targets = [...new Set(notifications.map(item => item.target))];
  return targets.map((target, index) => createPortal(
    <ToastStack notifications={notifications.filter(item => item.target === target)} />,
    target ?? document.body,
    String(index),
  ));
}

function ToastStack({ notifications }: { notifications: ErrorNotification[] }) {
  const region = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const node = region.current;
    node?.showPopover();
    return () => { if (node?.isConnected && node.matches(":popover-open")) node.hidePopover(); };
  }, []);
  return <div ref={region} popover="manual" className={styles.region} aria-label="Notifications">
    {notifications.map(item => <div key={item.id} className={styles.toast} role="alert" aria-atomic="true">
      <svg className={styles.icon} width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
        <circle cx="12" cy="12" r="9" /><path d="M12 7v6" strokeLinecap="round" /><circle cx="12" cy="17" r=".7" fill="currentColor" stroke="none" />
      </svg>
      <div className={styles.copy}><strong>{item.title}</strong><p>{item.message}</p></div>
      <button type="button" className={styles.dismiss} aria-label="Dismiss error" onClick={() => errorNotifications.dismiss(item.id)}>
        <svg width="16" height="16" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" aria-hidden="true"><path d="m5 5 10 10M15 5 5 15" /></svg>
      </button>
    </div>)}
  </div>;
}
