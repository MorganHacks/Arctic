"use client";

import { useId, useLayoutEffect, useRef, useState } from "react";
import { ArrowDown01Icon, Calendar03Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { dateLabel } from "./home-analytics";
import styles from "./home-date-range.module.css";

type DateRange = { from: string; to: string };

export function CustomDateRange({ value, min, max, selected, disabled, onApply }: {
  value: DateRange; min: string; max: string; selected: boolean; disabled: boolean;
  onApply: (value: DateRange) => void;
}) {
  const id = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const firstInput = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(value);
  const error = !draft.from || !draft.to ? "Choose a start and end date."
    : draft.from > draft.to ? "The end date must be on or after the start date."
    : draft.from < min || draft.to > max ? `Choose dates between ${dateLabel(min)} and ${dateLabel(max)}.`
    : "";

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
      panel.current.style.width = `${Math.min(320, width - 24)}px`;
      panel.current.style.maxHeight = `${height - 24}px`;
      panel.current.style.left = `${Math.max(left + 12, Math.min(rect.right - panel.current.offsetWidth, left + width - panel.current.offsetWidth - 12))}px`;
      panel.current.style.top = `${Math.max(top + 12, Math.min(rect.bottom + 8, top + height - panel.current.offsetHeight - 12))}px`;
    }

    position();
    firstInput.current?.focus({ preventScroll: true });
    const observer = new ResizeObserver(position);
    if (panel.current) observer.observe(panel.current);
    window.addEventListener("resize", position);
    window.addEventListener("scroll", position, true);
    window.visualViewport?.addEventListener("resize", position);
    window.visualViewport?.addEventListener("scroll", position);
    return () => {
      observer.disconnect();
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
      aria-haspopup="dialog" aria-expanded={open} aria-controls={id} aria-pressed={selected}
      disabled={disabled} title={selected ? `${dateLabel(value.from)} to ${dateLabel(value.to)}` : "Choose a date range"}
      onClick={() => { if (!open) setDraft(value); }}>
      <Icon icon={Calendar03Icon} size={16} /><span>Custom</span>
      <Icon icon={ArrowDown01Icon} size={14} className={styles.chevron} />
    </button>
    <div ref={panel} id={id} popover="auto" role="dialog" aria-labelledby={`${id}-title`}
      className={styles.panel} onToggle={(event) => setOpen(event.newState === "open")}
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
      <form onSubmit={(event) => {
        event.preventDefault();
        if (error) return;
        onApply(draft);
        close();
      }}>
        <h3 id={`${id}-title`}>Custom date range</h3>
        <p className={styles.hint}>Last 90 days · UTC</p>
        <div className={styles.fields}>
          <label htmlFor={`${id}-from`}>From</label>
          <input ref={firstInput} id={`${id}-from`} type="date" value={draft.from} min={min} max={max} required
            aria-invalid={Boolean(error)} aria-describedby={error ? `${id}-error` : undefined}
            onInput={(event) => { const from = event.currentTarget.value; setDraft((current) => ({ ...current, from })); }} />
          <label htmlFor={`${id}-to`}>To</label>
          <input id={`${id}-to`} type="date" value={draft.to} min={min} max={max} required
            aria-invalid={Boolean(error)} aria-describedby={error ? `${id}-error` : undefined}
            onInput={(event) => { const to = event.currentTarget.value; setDraft((current) => ({ ...current, to })); }} />
        </div>
        {error ? <p id={`${id}-error`} className={styles.error} role="status">{error}</p> : null}
        <div className={styles.actions}>
          <button type="button" onClick={close}>Cancel</button>
          <button type="submit" className={styles.apply} disabled={Boolean(error)}>Apply</button>
        </div>
      </form>
    </div>
  </>;
}
