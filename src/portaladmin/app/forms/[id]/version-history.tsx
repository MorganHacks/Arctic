"use client";

import { useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { Cancel01Icon, TimeScheduleIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { Avatar } from "@/components/ui/avatar";
import type { VersionRow } from "@/lib/api";
import { displayName } from "@/lib/person-profile";
import { loadVersionHistory } from "../actions";
import type { SaveStatus } from "./draft-autosave";
import styles from "./builder.module.css";

export function VersionHistory({ formId, versions, saveStatus }: {
  formId: string;
  versions: VersionRow[];
  saveStatus: SaveStatus;
}) {
  const id = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [loaded, setLoaded] = useState<VersionRow[] | null>(null);
  const [failed, setFailed] = useState(false);
  const shown = loaded ?? versions;

  useEffect(() => {
    if (!open || saveStatus === "dirty" || saveStatus === "saving") return;
    let current = true;
    void loadVersionHistory(formId).then(result => {
      if (!current) return;
      setFailed(result === null);
      if (result) setLoaded(result);
    });
    return () => { current = false; };
  }, [open, formId, saveStatus, versions]);

  useLayoutEffect(() => {
    if (!open) return;
    const position = () => {
      if (!trigger.current || !panel.current) return;
      const rect = trigger.current.getBoundingClientRect();
      const right = trigger.current.closest(`.${styles.canvasHistory}`)?.getBoundingClientRect().right ?? rect.right;
      panel.current.style.left = `${Math.max(12, Math.min(right - panel.current.offsetWidth, innerWidth - panel.current.offsetWidth - 12))}px`;
      panel.current.style.top = `${Math.max(12, Math.min(rect.bottom + 8, innerHeight - panel.current.offsetHeight - 12))}px`;
    };
    position();
    const observer = new ResizeObserver(position);
    if (panel.current) observer.observe(panel.current);
    window.addEventListener("resize", position);
    window.addEventListener("scroll", position, true);
    return () => {
      observer.disconnect();
      window.removeEventListener("resize", position);
      window.removeEventListener("scroll", position, true);
    };
  }, [open]);

  return <>
    <button type="button" ref={trigger} className={styles.headerIcon} popoverTarget={id}
      aria-label="Version history" title="Version history" aria-expanded={open} aria-haspopup="dialog" aria-controls={id}>
      <Icon icon={TimeScheduleIcon} size={19} />
    </button>
    <div ref={panel} id={id} popover="auto" role="dialog" aria-label="Version history"
      className={styles.historyPopover} onToggle={event => { if (event.target === event.currentTarget) setOpen(event.newState === "open"); }}>
      <div className={styles.themeHeading}>
        <h2>Version history</h2>
        <button type="button" className={styles.iconBtn} aria-label="Close version history" popoverTarget={id} popoverTargetAction="hide"><Icon icon={Cancel01Icon} size={18} /></button>
      </div>
      {failed ? <p className={styles.historyNotice} role="status">Couldn’t refresh history. Showing the last loaded versions.</p> : null}
      <ul>
        {shown.map(version => {
          const actor = version.actor;
          const name = actor ? displayName(actor.fullName, actor.email) : "Author not recorded";
          const action = version.publishedAt ? "Published" : version.updatedAt ? "Edited" : "Created";
          const date = version.publishedAt ?? version.updatedAt ?? version.createdAt;
          return <li key={version.version}>
            <div className={styles.versionHeading}>
              <div><strong>Version {version.version}</strong><span>{version.questions} questions</span></div>
              <span className={styles.versionStatus} data-state={version.status}>{version.status}</span>
            </div>
            <div className={styles.versionChange}>
              {actor ? <Avatar name={name} email={actor.email} avatarUrl={actor.avatarUrl} className={styles.versionAvatar} /> : null}
              <div>
                <p>{actor ? <><span>{action} by </span><strong title={actor.email}>{name}</strong></> : <span>{name}</span>}</p>
                <time dateTime={date} title={new Date(date).toLocaleString("en-US", { timeZoneName: "short" })}>
                  {new Date(date).toLocaleString("en-US", { month: "short", day: "numeric", year: "numeric", hour: "numeric", minute: "2-digit" })}
                </time>
              </div>
            </div>
          </li>;
        })}
      </ul>
      {!shown.length ? <p className={styles.historyNotice}>No versions recorded yet.</p> : null}
    </div>
  </>;
}
