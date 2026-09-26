"use client";

import { useId, useLayoutEffect, useRef, useState } from "react";
import { Cancel01Icon, Notification02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { EmptyState } from "@/components/ui/empty-state";
import styles from "./home-notifications.module.css";

const categories = [
  { id: "all", variant: "files", label: "All", title: "No notifications yet", description: "Your workspace updates will appear here." },
  { id: "applicants", variant: "data", label: "Applicants", title: "No applicant updates", description: "Notifications about applications will appear here." },
  { id: "forms", variant: "document", label: "Forms", title: "No form updates", description: "Notifications about your forms will appear here." },
  { id: "mail", variant: "document", label: "Mail", title: "No mail updates", description: "Notifications about your mail will appear here." },
  { id: "team", variant: "data", label: "Team", title: "No team updates", description: "Notifications about your team will appear here." },
] as const;

export function HomeNotifications() {
  const id = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const filters = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [selected, setSelected] = useState<(typeof categories)[number]["id"]>("all");
  const category = categories.find((item) => item.id === selected)!;

  useLayoutEffect(() => {
    if (!open) return;

    function position() {
      if (!trigger.current || !panel.current) return;
      const viewport = window.visualViewport;
      const left = viewport?.offsetLeft ?? 0;
      const top = viewport?.offsetTop ?? 0;
      const width = viewport?.width ?? document.documentElement.clientWidth;
      const height = viewport?.height ?? window.innerHeight;
      const rect = trigger.current.getBoundingClientRect();
      panel.current.style.width = `${Math.min(400, width - 24)}px`;
      panel.current.style.maxHeight = `${height - 24}px`;
      panel.current.style.left = `${Math.max(left + 12, Math.min(rect.right - panel.current.offsetWidth, left + width - panel.current.offsetWidth - 12))}px`;
      panel.current.style.top = `${Math.max(top + 12, Math.min(rect.bottom + 8, top + height - panel.current.offsetHeight - 12))}px`;
    }

    position();
    filters.current?.querySelector<HTMLButtonElement>("[aria-pressed='true']")?.focus({ preventScroll: true });
    window.addEventListener("resize", position);
    window.addEventListener("scroll", position, true);
    window.visualViewport?.addEventListener("resize", position);
    window.visualViewport?.addEventListener("scroll", position);
    return () => {
      window.removeEventListener("resize", position);
      window.removeEventListener("scroll", position, true);
      window.visualViewport?.removeEventListener("resize", position);
      window.visualViewport?.removeEventListener("scroll", position);
    };
  }, [open]);

  function close() {
    panel.current?.hidePopover();
    trigger.current?.focus({ preventScroll: true });
  }

  return <>
    <button ref={trigger} type="button" className={styles.trigger} popoverTarget={id}
      aria-label="Notifications" title="Notifications" aria-haspopup="dialog" aria-expanded={open} aria-controls={id}>
      <Icon icon={Notification02Icon} size={16} />
    </button>
    <div ref={panel} id={id} popover="auto" role="dialog" aria-labelledby={`${id}-title`} className={styles.panel}
      onToggle={(event) => setOpen(event.newState === "open")}
      onBlurCapture={(event) => {
        if (event.relatedTarget && !event.currentTarget.contains(event.relatedTarget) && event.relatedTarget !== trigger.current) {
          panel.current?.hidePopover();
        }
      }}
      onKeyDown={(event) => {
        if (event.key === "Escape") {
          event.preventDefault();
          event.stopPropagation();
          close();
        }
      }}>
      <div className={styles.heading}>
        <h2 id={`${id}-title`}>Notifications</h2>
        <button type="button" className={styles.close} onClick={close} aria-label="Close notifications">
          <Icon icon={Cancel01Icon} size={16} />
        </button>
      </div>
      <div className={styles.filterRow}>
        <div ref={filters} role="group" aria-label="Notification categories" className={styles.filters}>
          {categories.map((item) => <button key={item.id} type="button" aria-pressed={selected === item.id}
            aria-controls={`${id}-content`} className={styles.filter}
            onClick={(event) => {
              setSelected(item.id);
              event.currentTarget.scrollIntoView({ block: "nearest", inline: "nearest" });
            }}>
            <span>{item.label}</span><span className={styles.count}>0</span>
          </button>)}
        </div>
      </div>
      <div id={`${id}-content`} className={styles.content} role="status" aria-live="polite" aria-atomic="true">
        <EmptyState variant={category.variant} size="compact" title={category.title} description={category.description} />
      </div>
    </div>
  </>;
}
