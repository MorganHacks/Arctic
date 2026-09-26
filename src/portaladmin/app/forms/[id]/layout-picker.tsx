"use client";

import { useId, useLayoutEffect, useRef, useState } from "react";
import { Cancel01Icon, Layout2ColumnIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import type { FormTheme } from "../../../../../libs/ui/form-theme";
import shared from "./builder.module.css";
import popover from "./theme-picker.module.css";
import styles from "./layout-picker.module.css";

const layouts = [
  { value: "split", label: "Split", description: "Form details beside the questions." },
  { value: "cards", label: "Cards", description: "Centered cards, just like the preview." },
] as const;

export function LayoutPicker({ theme, onChange, disabled }: {
  theme: FormTheme;
  onChange: (theme: FormTheme) => void;
  disabled: boolean;
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
      panel.current.style.maxHeight = `${Math.max(120, innerHeight - rect.bottom - 22)}px`;
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
    <button type="button" ref={trigger} className={shared.headerIcon} popoverTarget={id}
      aria-label="Form layout" title="Form layout" aria-haspopup="dialog" aria-controls={id} aria-expanded={open} disabled={disabled}>
      <Icon icon={Layout2ColumnIcon} size={20} />
    </button>
    <div id={id} ref={panel} popover="auto" className={`${popover.panel} ${styles.panel}`} role="dialog"
      aria-labelledby={`${id}-heading`} onToggle={event => {
        if (event.target === event.currentTarget) setOpen(event.newState === "open");
      }}>
      <div className={popover.heading}>
        <h2 id={`${id}-heading`}>Form layout</h2>
        <button type="button" className={popover.close} aria-label="Close layout settings" popoverTarget={id} popoverTargetAction="hide">
          <Icon icon={Cancel01Icon} size={17} />
        </button>
      </div>
      <div className={`${popover.panelBody} ${styles.body}`}>
        <p className={styles.description}>Choose how your form appears to responders.</p>
        <fieldset className={styles.options} disabled={disabled}>
          <legend className={popover.srOnly}>Responder layout</legend>
          {layouts.map(layout => <label key={layout.value} className={styles.option}>
            <span className={styles.sample} data-layout={layout.value} aria-hidden="true">
              <span className={styles.sampleIntro}><i /><i /></span>
              <span className={styles.sampleQuestions}><i /><i /><i /></span>
            </span>
            <span className={styles.label}>
              <input type="radio" name={`${id}-layout`} value={layout.value} checked={(theme.layout ?? "split") === layout.value}
                onChange={() => onChange({ ...theme, layout: layout.value })} />
              {layout.label}
            </span>
            <span className={styles.hint}>{layout.description}</span>
          </label>)}
        </fieldset>
        <p className={styles.note}>Saved to your draft. Publish changes to update the live form.</p>
      </div>
    </div>
  </>;
}
