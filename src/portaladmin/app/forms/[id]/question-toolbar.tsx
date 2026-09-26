"use client";

import { useId, useLayoutEffect, useRef, useState } from "react";
import { Add01Icon, ArrowUp01Icon, Cancel01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import type { FieldType } from "@/lib/api";
import { TYPES } from "./fields";
import { PageBreakIcon, TypeIcon } from "./icons";
import styles from "./question-toolbar.module.css";

const groups: { key: string; title: string; types: FieldType[] }[] = [
  { key: "answers", title: "Text & choices", types: ["shortText", "paragraph", "select", "radio", "checkboxes"] },
  { key: "details", title: "Details & uploads", types: ["email", "phone", "number", "date", "consent", "file"] },
];

const descriptions: Partial<Record<FieldType, string>> = {
  shortText: "Single-line answer",
  paragraph: "Long-form answer",
  select: "Pick from a list",
  radio: "Pick one option",
  checkboxes: "Pick multiple options",
  email: "Email address",
  phone: "Phone number",
  number: "Numeric answer",
  date: "Calendar date",
  consent: "Accept a statement",
  file: "Attach a file",
};

export function QuestionToolbar({ onAdd, onAddSection, disabled }: {
  onAdd: (type: FieldType) => void;
  onAddSection: () => void;
  disabled: boolean;
}) {
  const id = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const picker = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);

  useLayoutEffect(() => {
    if (!open) return;
    const position = (event?: Event) => {
      if (!trigger.current || !picker.current || event?.target === picker.current) return;
      const rect = trigger.current.parentElement!.getBoundingClientRect();
      const halfWidth = picker.current.offsetWidth / 2;
      picker.current.style.left = `${Math.max(halfWidth + 12, Math.min(rect.left + rect.width / 2, innerWidth - halfWidth - 12))}px`;
      picker.current.style.bottom = `${innerHeight - rect.top + 12}px`;
      picker.current.style.maxHeight = `${Math.max(80, rect.top - 24)}px`;
    };
    const observer = new ResizeObserver(() => position());
    if (picker.current) observer.observe(picker.current);
    if (trigger.current?.parentElement) observer.observe(trigger.current.parentElement);
    position();
    picker.current?.querySelector<HTMLButtonElement>("button[data-question-type]")?.focus({ preventScroll: true });
    window.addEventListener("resize", position);
    window.addEventListener("scroll", position, true);
    return () => {
      observer.disconnect();
      window.removeEventListener("resize", position);
      window.removeEventListener("scroll", position, true);
    };
  }, [open]);

  return <>
    <div className={styles.toolbar} role="group" aria-label="Add form content">
      <button ref={trigger} type="button" popoverTarget={id} disabled={disabled}
        aria-expanded={open} aria-haspopup="dialog" aria-controls={id}>
        <Icon icon={Add01Icon} size={17} />Add question<Icon icon={ArrowUp01Icon} size={13} />
      </button>
      <span className={styles.divider} aria-hidden="true" />
      <button type="button" disabled={disabled} aria-label="Add page break" onClick={onAddSection}>
        <PageBreakIcon size={16} />Page break
      </button>
    </div>
    <div ref={picker} id={id} popover="auto" role="dialog" aria-label="Choose question type" className={styles.picker}
      onToggle={event => { if (event.target === event.currentTarget) setOpen(event.newState === "open"); }}>
      <div className={styles.heading}>
        <div><h2>Add a question</h2><p>Choose how people respond.</p></div>
        <button type="button" className={styles.close} aria-label="Close question picker"
          onClick={() => { picker.current?.hidePopover(); trigger.current?.focus({ preventScroll: true }); }}>
          <Icon icon={Cancel01Icon} size={16} />
        </button>
      </div>
      <div className={styles.groups}>
        {groups.map(group => <section key={group.key} className={styles.group} aria-labelledby={`${id}-${group.key}`}>
          <h3 id={`${id}-${group.key}`}>{group.title}</h3>
          <div className={styles.types}>
            {TYPES.filter(type => group.types.includes(type.value)).map(type => <button type="button" key={type.value}
              disabled={disabled} data-question-type={type.value} aria-label={type.label} aria-describedby={`${id}-${type.value}-description`}
              onClick={() => { picker.current?.hidePopover(); onAdd(type.value); }}>
              <span className={styles.typeIcon}><TypeIcon type={type.value} /></span>
              <span className={styles.typeText}><span className={styles.typeLabel}>{type.label}</span>
                <span id={`${id}-${type.value}-description`} className={styles.typeDescription}>{descriptions[type.value]}</span>
              </span>
            </button>)}
          </div>
        </section>)}
      </div>
    </div>
  </>;
}
