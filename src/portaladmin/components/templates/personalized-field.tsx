"use client";

import dynamic from "next/dynamic";
import { useId, useLayoutEffect, useRef, useState } from "react";
import { Cancel01Icon, SmileIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { PlaceholderField } from "./placeholder-field";
import { SubjectCustomFields } from "./subject-custom-fields";
import type { Placeholder } from "./types";
import styles from "./subject-field.module.css";

const EmojiPicker = dynamic(() => import("./subject-emoji-picker"), {
  loading: () => <p className={styles.empty}>Loading emoji…</p>,
  ssr: false,
});

export function PersonalizedField({ id, label, placeholder, describedBy, value, onChange, available, className, validationError }: {
  id: string;
  label: string;
  placeholder: string;
  describedBy?: string;
  value: string;
  onChange: (value: string) => void;
  available: Placeholder[] | null;
  className: string;
  validationError?: string;
}) {
  const pickerId = useId();
  const field = useRef<HTMLTextAreaElement | HTMLInputElement | null>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const popover = useRef<HTMLDivElement>(null);
  const selection = useRef({ start: value.length, end: value.length });
  const pendingCaret = useRef<number | null>(null);
  const [open, setOpen] = useState(false);
  const [error, setError] = useState("");

  useLayoutEffect(() => {
    if (pendingCaret.current !== null) {
      field.current?.focus();
      field.current?.setSelectionRange(pendingCaret.current, pendingCaret.current);
      pendingCaret.current = null;
    }
  });

  useLayoutEffect(() => {
    if (!open) {
      return;
    }

    function position() {
      const button = trigger.current;
      const panel = popover.current;
      if (!button || !panel) {
        return;
      }

      const rect = button.getBoundingClientRect();
      const viewport = window.visualViewport;
      const viewportTop = viewport?.offsetTop ?? 0;
      const viewportLeft = viewport?.offsetLeft ?? 0;
      const viewportWidth = viewport?.width ?? window.innerWidth;
      const viewportBottom = viewportTop + (viewport?.height ?? window.innerHeight);
      const width = Math.min(320, viewportWidth - 24);
      const below = viewportBottom - rect.bottom - 20;
      const above = rect.top - viewportTop - 20;
      const showBelow = below >= 360 || below >= above;
      const height = Math.min(360, Math.max(180, showBelow ? below : above));

      panel.style.width = `${width}px`;
      panel.style.height = `${height}px`;
      panel.style.left = `${Math.max(viewportLeft + 12, Math.min(rect.right - width, viewportLeft + viewportWidth - width - 12))}px`;
      panel.style.top = `${Math.max(viewportTop + 12, showBelow ? rect.bottom + 8 : rect.top - height - 8)}px`;
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
  }, [open]);

  function rememberSelection() {
    selection.current = {
      start: field.current?.selectionStart ?? value.length,
      end: field.current?.selectionEnd ?? value.length,
    };
    setError("");
  }

  function insertText(text: string) {
    const { start, end } = selection.current;
    const next = value.slice(0, start) + text + value.slice(end);
    if (next.length > 200) {
      setError(`${label} can be up to 200 characters.`);
      return false;
    }

    pendingCaret.current = start + text.length;
    onChange(next);
    return true;
  }

  return (
    <div className={styles.wrapper}>
      <PlaceholderField
        id={id}
        value={value}
        onChange={onChange}
        available={available}
        inputRef={field}
        placeholder={placeholder}
        className={`${className} ${styles.input}`}
        maxLength={200}
        invalid={Boolean(validationError)}
        describedBy={describedBy}
      />
      <div className={styles.actions}>
        <SubjectCustomFields available={available} label={label} onOpen={rememberSelection} onSelect={insertText} error={error} />
        <button
          ref={trigger}
          type="button"
          className={styles.trigger}
          popoverTarget={pickerId}
          aria-label={open ? `Close ${label.toLowerCase()} emoji picker` : `Insert emoji into ${label.toLowerCase()}`}
          aria-haspopup="dialog"
          aria-expanded={open}
          aria-controls={pickerId}
          onClick={rememberSelection}
        >
          <Icon icon={open ? Cancel01Icon : SmileIcon} size={20} />
        </button>
      </div>
      <div
        ref={popover}
        id={pickerId}
        popover="auto"
        role="dialog"
        aria-label="Choose an emoji"
        className={styles.popover}
        onToggle={(event) => setOpen(event.newState === "open")}
        onBlurCapture={(event) => {
          if (event.relatedTarget && !event.currentTarget.contains(event.relatedTarget) && event.relatedTarget !== trigger.current) {
            popover.current?.hidePopover();
          }
        }}
      >
        {open ? <EmojiPicker onSelect={(emoji) => {
          if (insertText(emoji)) {
            popover.current?.hidePopover();
          }
        }} error={error} /> : null}
      </div>
    </div>
  );
}
