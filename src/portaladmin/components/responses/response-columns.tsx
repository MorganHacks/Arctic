"use client";

import { useEffect, useId, useRef } from "react";
import { Layout3ColumnIcon, Cancel01Icon, Tick02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import type { Column } from "./columns";
import styles from "./responses.module.css";

export function ResponseColumns({ columns, selected, onChange }: {
  columns: Column[];
  selected: Set<string>;
  onChange: (keys: Set<string>) => void;
}) {
  const id = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const reposition = () => { if (panel.current?.matches(":popover-open")) position(); };
    window.addEventListener("resize", reposition);
    return () => window.removeEventListener("resize", reposition);
  }, []);

  function position() {
    if (!trigger.current || !panel.current) return;
    const rect = trigger.current.getBoundingClientRect();
    panel.current.style.top = `${rect.bottom + 8}px`;
    panel.current.style.left = `${Math.max(12, Math.min(rect.right - panel.current.offsetWidth, innerWidth - panel.current.offsetWidth - 12))}px`;
    panel.current.style.maxHeight = `${Math.min(480, Math.max(140, innerHeight - rect.bottom - 24))}px`;
  }

  return (
    <>
      <button ref={trigger} type="button" className={styles.secondaryButton} popoverTarget={id}>
        <Icon icon={Layout3ColumnIcon} size={17} />Columns
      </button>
      <div ref={panel} id={id} popover="auto" className={styles.columnOptions}
        onToggle={(event) => { if (event.newState === "open") position(); }}>
        <div className={styles.columnsHead}>
          <strong>Visible answers</strong>
          <button type="button" className={styles.iconButton} aria-label="Close columns" popoverTarget={id} popoverTargetAction="hide">
            <Icon icon={Cancel01Icon} size={17} />
          </button>
        </div>
        <p>Choose which answers appear.</p>
        <div className={styles.columnList}>
          {columns.map((column) => (
            <label key={column.key}>
              <input type="checkbox" checked={selected.has(column.key)} onChange={(event) => {
                const next = new Set(selected);
                if (event.target.checked) next.add(column.key); else next.delete(column.key);
                onChange(next);
              }} />
              <span className={styles.columnCheck} aria-hidden="true"><Icon icon={Tick02Icon} size={13} strokeWidth={2.2} /></span>
              <span>{column.label}{column.kind === "retired" ? <small>Removed question</small> : null}</span>
            </label>
          ))}
        </div>
        <div className={styles.columnsFoot}>
          <button type="button" onClick={() => onChange(new Set(columns.map((column) => column.key)))}>Show all</button>
          <button type="button" onClick={() => onChange(new Set())}>Hide all</button>
        </div>
      </div>
    </>
  );
}
