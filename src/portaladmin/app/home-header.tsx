"use client";

import { useId, useLayoutEffect, useRef, useState } from "react";
import { ArrowUpDownIcon, Building03Icon, Globe02Icon, Tick02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { HomeNotifications } from "./home-notifications";
import styles from "./home-header.module.css";

export function HomeHeader({ teams }: { teams: string[] }) {
  const id = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const selection = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState(false);

  useLayoutEffect(() => {
    if (!open) return;

    function position() {
      if (!trigger.current || !panel.current) return;
      const rect = trigger.current.getBoundingClientRect();
      const width = document.documentElement.clientWidth;
      panel.current.style.left = `${Math.max(12, Math.min(rect.right - panel.current.offsetWidth, width - panel.current.offsetWidth - 12))}px`;
      panel.current.style.top = `${Math.max(12, Math.min(rect.bottom + 8, window.innerHeight - panel.current.offsetHeight - 12))}px`;
    }

    position();
    selection.current?.focus({ preventScroll: true });
    window.addEventListener("resize", position);
    window.addEventListener("scroll", position, true);
    return () => {
      window.removeEventListener("resize", position);
      window.removeEventListener("scroll", position, true);
    };
  }, [open]);

  function close() {
    panel.current?.hidePopover();
    trigger.current?.focus();
  }

  return (
    <header className={styles.header}>
      <nav aria-label="Breadcrumb" className={styles.breadcrumb}>
        <Icon icon={Globe02Icon} size={16} />
        <ol>
          <li>My account</li>
          <li className={styles.separator} aria-hidden="true">/</li>
          <li aria-current="page">Dashboard</li>
        </ol>
      </nav>
      <div className={styles.actions}>
        <button ref={trigger} type="button" className={styles.workspace} popoverTarget={id}
          aria-label="MorganHacks workspace" aria-haspopup="dialog" aria-expanded={open} aria-controls={id}>
          <span className={styles.workspaceIcon}><Icon icon={Building03Icon} size={16} /></span>
          <span className={styles.workspaceName}>MorganHacks</span>
          <Icon icon={ArrowUpDownIcon} size={14} className={styles.chevron} />
        </button>
        <div ref={panel} id={id} popover="auto" role="dialog" aria-label="Your workspace" className={styles.popover}
          onToggle={(event) => setOpen(event.newState === "open")}
          onBlurCapture={(event) => {
            if (event.relatedTarget && !event.currentTarget.contains(event.relatedTarget) && event.relatedTarget !== trigger.current) {
              panel.current?.hidePopover();
            }
          }}
          onKeyDown={(event) => {
            if (event.key === "Escape") {
              event.preventDefault();
              close();
            }
          }}>
          <p className={styles.popoverLabel}>Your workspace</p>
          <button ref={selection} type="button" className={styles.currentWorkspace} onClick={close} aria-current="true">
            <span className={styles.workspaceIcon}><Icon icon={Building03Icon} size={18} /></span>
            <span><strong>MorganHacks</strong><small>Organizer workspace</small></span>
            <Icon icon={Tick02Icon} size={17} className={styles.check} />
          </button>
          {teams.length > 0 ? (
            <div className={styles.teamSection}>
              <p className={styles.popoverLabel}>Your teams</p>
              <ul className={styles.teams}>
                {teams.map((team) => <li key={team}>{team.replaceAll("-", " ")}</li>)}
              </ul>
            </div>
          ) : null}
        </div>
        <HomeNotifications />
      </div>
    </header>
  );
}
