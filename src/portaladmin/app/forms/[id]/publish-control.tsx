"use client";

import { useId, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { ArrowDown01Icon, Cancel01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import styles from "./builder.module.css";

export function PublishControl({ published, publishing, canManage, onPublish, onUnpublish, children }: {
  published: boolean;
  publishing: boolean;
  canManage: boolean;
  onPublish: () => void;
  onUnpublish: () => void;
  children: ReactNode;
}) {
  const id = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);

  useLayoutEffect(() => {
    if (!open) return;
    const position = () => {
      if (!trigger.current || !panel.current) return;
      const rect = trigger.current.getBoundingClientRect();
      panel.current.style.left = `${Math.max(12, Math.min(rect.right - panel.current.offsetWidth, innerWidth - panel.current.offsetWidth - 12))}px`;
      panel.current.style.top = `${rect.bottom + 10}px`;
      panel.current.style.maxHeight = `${innerHeight - rect.bottom - 22}px`;
    };
    position();
    window.addEventListener("resize", position);
    window.addEventListener("scroll", position, true);
    return () => {
      window.removeEventListener("resize", position);
      window.removeEventListener("scroll", position, true);
    };
  }, [open]);

  return <>
    <div className={styles.publishSplit} data-disabled={publishing || !canManage}>
      <button type="button" className={styles.publishAction} disabled={publishing || !canManage}
        onClick={() => {
          panel.current?.hidePopover();
          if (published) onUnpublish();
          else onPublish();
        }}>
        {publishing ? "Publishing…" : published ? "Unpublish" : "Publish"}
      </button>
      <button ref={trigger} type="button" className={styles.publishArrow} aria-label="Publishing options"
        aria-haspopup="dialog" aria-expanded={open} aria-controls={id} popoverTarget={id} disabled={publishing}>
        <Icon icon={ArrowDown01Icon} size={18} />
      </button>
    </div>
    <div ref={panel} id={id} popover="auto" role="dialog" aria-label="Publishing options" className={styles.publishPanel}
      onToggle={event => { if (event.target === event.currentTarget) setOpen(event.newState === "open"); }}>
      <div className={styles.publishHeading}>
        <div><h2>Publishing options</h2><p>Manage access and availability.</p></div>
        <button type="button" className={styles.iconBtn} aria-label="Close publishing options" popoverTarget={id} popoverTargetAction="hide"><Icon icon={Cancel01Icon} size={18} /></button>
      </div>
      <div className={styles.publishSettings}>{children}</div>
      {published ? <div className={styles.publishChanges}>
        <button type="button" className="primary" disabled={publishing || !canManage} onClick={() => {
          panel.current?.hidePopover();
          onPublish();
        }}>Publish changes</button>
        <p>Updates your live form with the latest draft.</p>
      </div> : null}
    </div>
  </>;
}
