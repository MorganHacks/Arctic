"use client";

import { useEffect, useId, useRef, useState, type ButtonHTMLAttributes, type RefObject } from "react";
import type { FormField } from "@/lib/api";

export type DragHandleProps = ButtonHTMLAttributes<HTMLButtonElement>;
type DragView = { key: string; label: string; x: number; y: number; overKey?: string; edge?: "before" | "after" };
type PointerDrag = { key: string; pointerId: number; handle: HTMLButtonElement; startX: number; startY: number; x: number; y: number; moving: boolean; to: number };

export function useQuestionDrag({ fields, list, canvas, disabled, onReorder }: {
  fields: FormField[];
  list: RefObject<HTMLOListElement | null>;
  canvas: RefObject<HTMLDivElement | null>;
  disabled: boolean;
  onReorder: (key: string, to: number) => void;
}) {
  const instructionId = useId();
  const latest = useRef({ fields, disabled, onReorder });
  latest.current = { fields, disabled, onReorder };
  const pointer = useRef<PointerDrag | null>(null);
  const frame = useRef(0);
  const [drag, setDrag] = useState<DragView | null>(null);
  const [announcement, setAnnouncement] = useState("");

  const announceMove = (key: string, to: number) => {
    const current = latest.current;
    const from = current.fields.findIndex(field => field.key === key);
    if (current.disabled || from < 0 || from === to) return;
    current.onReorder(key, to);
    setAnnouncement(`${current.fields[from].label || "Untitled question"} moved to position ${to + 1} of ${current.fields.length}.`);
  };

  const finish = (commit: boolean) => {
    const current = pointer.current;
    pointer.current = null;
    cancelAnimationFrame(frame.current);
    setDrag(null);
    if (!current) return;
    if (current.handle.hasPointerCapture(current.pointerId)) current.handle.releasePointerCapture(current.pointerId);
    const bounds = canvas.current?.getBoundingClientRect();
    const inside = bounds && current.x >= bounds.left && current.x <= bounds.right && current.y >= bounds.top && current.y <= bounds.bottom;
    if (commit && inside && current.moving) announceMove(current.key, current.to);
    else if (current.moving) setAnnouncement("Move cancelled.");
  };
  const cancel = useRef(() => finish(false));
  cancel.current = () => finish(false);

  useEffect(() => {
    const escape = (event: KeyboardEvent) => {
      if (event.key === "Escape" && pointer.current) {
        event.preventDefault();
        event.stopImmediatePropagation();
        cancel.current();
      }
    };
    const blur = () => cancel.current();
    window.addEventListener("keydown", escape, true);
    window.addEventListener("blur", blur);
    return () => {
      cancelAnimationFrame(frame.current);
      window.removeEventListener("keydown", escape, true);
      window.removeEventListener("blur", blur);
    };
  }, []);

  const track = () => {
    const current = pointer.current;
    const container = canvas.current;
    if (!current?.moving || !container || !list.current) return;
    const bounds = container.getBoundingClientRect();
    const withinWidth = current.x >= bounds.left && current.x <= bounds.right;
    if (withinWidth) {
      const edge = 64;
      const speed = current.y < bounds.top + edge
        ? -Math.min(14, Math.max(0, (bounds.top + edge - current.y) / edge * 14))
        : Math.min(14, Math.max(0, (current.y - bounds.bottom + edge) / edge * 14));
      container.scrollTop += speed;
    }
    const cards = Array.from(list.current.children).filter((card): card is HTMLElement => card instanceof HTMLElement && card.dataset.key !== current.key);
    const before = cards.findIndex(card => {
      const rect = card.getBoundingClientRect();
      return current.y < rect.top + rect.height / 2;
    });
    current.to = before < 0 ? cards.length : before;
    const original = latest.current.fields.findIndex(field => field.key === current.key);
    const target = cards[before < 0 ? cards.length - 1 : before];
    const inside = withinWidth && current.y >= bounds.top && current.y <= bounds.bottom;
    setDrag({
      key: current.key,
      label: latest.current.fields.find(field => field.key === current.key)?.label || "Untitled question",
      x: Math.max(12, Math.min(current.x - 100, innerWidth - 252)),
      y: Math.max(12, Math.min(current.y + 16, innerHeight - 64)),
      overKey: inside && original !== current.to ? target?.dataset.key : undefined,
      edge: before < 0 ? "after" : "before",
    });
    frame.current = requestAnimationFrame(track);
  };

  const handleProps = (field: FormField, index: number): DragHandleProps => ({
    disabled: disabled || fields.length < 2,
    "aria-label": `Move ${field.type === "section" ? "page break" : "question"}: ${field.label || "Untitled question"}`,
    "aria-describedby": instructionId,
    title: "Drag to reorder",
    onPointerDown: event => {
      if (event.button !== 0 || !event.isPrimary || latest.current.disabled || pointer.current) return;
      event.preventDefault();
      event.currentTarget.focus({ preventScroll: true });
      event.currentTarget.setPointerCapture(event.pointerId);
      pointer.current = { key: field.key, pointerId: event.pointerId, handle: event.currentTarget, startX: event.clientX, startY: event.clientY, x: event.clientX, y: event.clientY, moving: false, to: index };
    },
    onPointerMove: event => {
      const current = pointer.current;
      if (!current || current.pointerId !== event.pointerId) return;
      current.x = event.clientX;
      current.y = event.clientY;
      if (!current.moving && Math.hypot(current.x - current.startX, current.y - current.startY) >= 6) {
        current.moving = true;
        setAnnouncement(`Moving ${field.label || "Untitled question"}. Release to place it, or press Escape to cancel.`);
        track();
      }
    },
    onPointerUp: event => {
      const current = pointer.current;
      if (!current || current.pointerId !== event.pointerId) return;
      current.x = event.clientX;
      current.y = event.clientY;
      if (current.moving) {
        cancelAnimationFrame(frame.current);
        track();
      }
      finish(true);
    },
    onPointerCancel: () => finish(false),
    onLostPointerCapture: () => { if (pointer.current) finish(false); },
    onKeyDown: event => {
      if (pointer.current || event.altKey || event.metaKey || event.ctrlKey) return;
      const to = event.key === "ArrowUp" ? index - 1 : event.key === "ArrowDown" ? index + 1 : event.key === "Home" ? 0 : event.key === "End" ? fields.length - 1 : null;
      if (to === null) return;
      event.preventDefault();
      if (to >= 0 && to < fields.length) announceMove(field.key, to);
    },
  });

  return { drag, announcement, instructionId, handleProps };
}
