"use client";

import { useId, useLayoutEffect, useRef, useState } from "react";
import { ArrowDown01Icon, Search01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import type { Placeholder } from "./types";
import styles from "./subject-field.module.css";

function fieldLabel(name: string) {
  const words = name.replace(/([a-z\d])([A-Z])/g, "$1 $2").replace(/[_.]/g, " ").toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

export function SubjectCustomFields({ available, label, onOpen, onSelect, error }: {
  available: Placeholder[] | null;
  label: string;
  onOpen: () => void;
  onSelect: (text: string) => boolean;
  error: string;
}) {
  const id = useId();
  const listId = `${id}-list`;
  const trigger = useRef<HTMLButtonElement>(null);
  const popover = useRef<HTMLDivElement>(null);
  const search = useRef<HTMLInputElement>(null);
  const list = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const fields = available ?? [];
  const wanted = query.trim().toLowerCase();
  const matches = fields.filter((item) => item.name.toLowerCase().includes(wanted) || fieldLabel(item.name).toLowerCase().includes(wanted));

  useLayoutEffect(() => {
    if (!open) return;

    function position() {
      const button = trigger.current;
      const panel = popover.current;
      if (!button || !panel) return;

      const rect = button.getBoundingClientRect();
      const viewport = window.visualViewport;
      const top = viewport?.offsetTop ?? 0;
      const left = viewport?.offsetLeft ?? 0;
      const width = viewport?.width ?? window.innerWidth;
      const bottom = top + (viewport?.height ?? window.innerHeight);
      const below = bottom - rect.bottom - 20;
      const above = rect.top - top - 20;
      const showBelow = below >= 360 || below >= above;

      panel.style.width = `${Math.min(330, width - 24)}px`;
      panel.style.maxHeight = `${Math.min(380, Math.max(120, showBelow ? below : above))}px`;
      panel.style.left = `${Math.max(left + 12, Math.min(rect.right - panel.offsetWidth, left + width - panel.offsetWidth - 12))}px`;
      panel.style.top = `${Math.max(top + 12, showBelow ? rect.bottom + 8 : rect.top - panel.offsetHeight - 8)}px`;
      panel.style.transformOrigin = showBelow ? "top right" : "bottom right";
    }

    position();
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
  }, [open, matches.length, error]);

  useLayoutEffect(() => {
    if (open) search.current?.focus({ preventScroll: true });
  }, [open]);

  useLayoutEffect(() => {
    if (open) list.current?.children[active]?.scrollIntoView({ block: "nearest" });
  }, [active, open]);

  function choose(item: Placeholder) {
    if (onSelect(`{{${item.name}}}`)) popover.current?.hidePopover();
  }

  return (
    <>
      <button
        ref={trigger}
        type="button"
        className={styles.fieldsTrigger}
        popoverTarget={id}
        aria-label={`Custom fields for ${label.toLowerCase()}`}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={id}
        disabled={fields.length === 0}
        title={available === null ? "Custom fields are currently unavailable" : fields.length === 0 ? "No custom fields available" : undefined}
        onClick={onOpen}
        onKeyDown={(event) => {
          if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            onOpen();
            popover.current?.showPopover();
          }
        }}
      >
        Custom fields
        <Icon icon={ArrowDown01Icon} size={14} />
      </button>
      <div
        ref={popover}
        id={id}
        popover="auto"
        role="dialog"
        aria-label={`Custom fields for ${label.toLowerCase()}`}
        className={`${styles.popover} ${styles.fieldsPopover}`}
        onToggle={(event) => {
          setOpen(event.newState === "open");
          if (event.newState === "open") {
            setQuery("");
            setActive(0);
          }
        }}
        onBlurCapture={(event) => {
          if (event.relatedTarget && !event.currentTarget.contains(event.relatedTarget) && event.relatedTarget !== trigger.current) {
            popover.current?.hidePopover();
          }
        }}
        onKeyDown={(event) => {
          if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            popover.current?.hidePopover();
            trigger.current?.focus();
          }
        }}
      >
        <div className={styles.fieldsSearch}>
          <Icon icon={Search01Icon} size={16} />
          <input
            ref={search}
            type="search"
            role="combobox"
            aria-label="Search custom fields"
            aria-autocomplete="list"
            aria-expanded={open}
            aria-controls={listId}
            aria-activedescendant={open && matches.length > 0 ? `${listId}-${active}` : undefined}
            placeholder="Search fields…"
            autoComplete="off"
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setActive(0);
            }}
            onKeyDown={(event) => {
              if (matches.length === 0) return;
              if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();
                setActive((index) => (index + (event.key === "ArrowDown" ? 1 : -1) + matches.length) % matches.length);
              } else if (event.key === "Enter") {
                event.preventDefault();
                choose(matches[active]);
              }
            }}
          />
        </div>
        <div ref={list} id={listId} role="listbox" aria-label="Available custom fields" className={styles.fieldsList}>
          {matches.map((item, index) => (
            <div
              key={item.name}
              id={`${listId}-${index}`}
              role="option"
              aria-selected={index === active}
              title={item.description ?? undefined}
              className={styles.fieldOption}
              onMouseDown={(event) => event.preventDefault()}
              onMouseMove={() => setActive(index)}
              onClick={() => choose(item)}
            >
              <span className={styles.fieldName}>{fieldLabel(item.name)}</span>
              <span className={styles.fieldToken}>{`{{${item.name}}}`}</span>
            </div>
          ))}
        </div>
        {matches.length === 0 ? <p className={styles.fieldsEmpty} role="status">No fields found.</p> : null}
        {error ? <p className={styles.fieldsError} role="alert">{error}</p> : null}
      </div>
    </>
  );
}
