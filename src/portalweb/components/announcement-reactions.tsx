"use client";

import { useEffect, useId, useLayoutEffect, useRef, useState, type CSSProperties, type KeyboardEvent } from "react";
import { createPortal } from "react-dom";
import { announcementReactions, emptyReactions, type AnnouncementReaction, type AnnouncementReactions as ReactionState } from "../../../libs/ui/announcement-reactions";
import styles from "../../../libs/ui/announcement-reactions.module.css";

export function AnnouncementReactions({ id, initial }: { id: string; initial: ReactionState }) {
  const [reactions, setReactions] = useState(initial ?? emptyReactions);
  const [open, setOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const [unavailable, setUnavailable] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState("");
  const [position, setPosition] = useState({ top: 0, left: 0, anchor: 0, side: "top" });
  const [burst, setBurst] = useState<{ key: number; emoji: string; x: number; y: number } | null>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const picker = useRef<HTMLDivElement>(null);
  const options = useRef<(HTMLButtonElement | null)[]>([]);
  const saving = useRef(false);
  const menuId = useId();
  const choice = announcementReactions.find(reaction => reaction.key === reactions.choice);

  useEffect(() => { if (!saving.current) setReactions(initial ?? emptyReactions()); }, [initial]);
  useEffect(() => {
    if (!burst) return;
    const timer = window.setTimeout(() => setBurst(null), 1200);
    return () => window.clearTimeout(timer);
  }, [burst]);

  useLayoutEffect(() => {
    if (!open) return;
    function place() {
      if (!trigger.current || !picker.current) return;
      const anchor = trigger.current.getBoundingClientRect();
      if (anchor.bottom < 0 || anchor.top > window.innerHeight) { setOpen(false); return; }
      const width = picker.current.offsetWidth;
      const height = picker.current.offsetHeight;
      const left = Math.max(12, Math.min(anchor.left + anchor.width / 2 - width / 2, window.innerWidth - width - 12));
      const above = anchor.top >= height + 36;
      setPosition({ left, top: above ? anchor.top - height - 12 : anchor.bottom + 12,
        anchor: Math.max(18, Math.min(anchor.left + anchor.width / 2 - left, width - 18)), side: above ? "top" : "bottom" });
    }
    place();
    const selected = announcementReactions.findIndex(item => item.key === reactions.choice);
    options.current[Math.max(selected, 0)]?.focus({ preventScroll: true });
    window.addEventListener("resize", place);
    window.addEventListener("scroll", place, true);
    return () => { window.removeEventListener("resize", place); window.removeEventListener("scroll", place, true); };
  }, [open, reactions.choice]);

  useEffect(() => {
    if (!open) return;
    function outside(event: PointerEvent) {
      if (event.target instanceof Node && !picker.current?.contains(event.target) && !trigger.current?.contains(event.target)) setOpen(false);
    }
    function escape(event: globalThis.KeyboardEvent) {
      if (event.key === "Escape") { event.preventDefault(); setOpen(false); trigger.current?.focus(); }
    }
    document.addEventListener("pointerdown", outside);
    document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("pointerdown", outside); document.removeEventListener("keydown", escape); };
  }, [open]);

  async function react(key: AnnouncementReaction, element: HTMLButtonElement) {
    if (saving.current || unavailable) return;
    const next = reactions.choice === key ? null : key;
    const previous = reactions;
    const rect = element.getBoundingClientRect();
    const counts = { ...previous.counts };
    if (previous.choice) counts[previous.choice] = Math.max(0, counts[previous.choice] - 1);
    if (next) counts[next] += 1;
    saving.current = true;
    setPending(true);
    setOpen(false);
    setError(null);
    setStatus("");
    trigger.current?.focus({ preventScroll: true });
    setReactions({ counts, choice: next, total: Object.values(counts).reduce((sum, count) => sum + count, 0) });
    if (next) setBurst({ key: Date.now(), emoji: announcementReactions.find(item => item.key === next)!.emoji, x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 });
    try {
      const response = await fetch(`/api/portal/announcements/${encodeURIComponent(id)}/reaction`, {
        method: next ? "PUT" : "DELETE",
        ...(next ? { headers: { "content-type": "application/json" }, body: JSON.stringify({ reaction: next }) } : {}),
      });
      const data = await response.json().catch(() => ({})) as { reactions?: ReactionState; error?: string };
      if (!response.ok || !data.reactions) {
        if (response.status === 404 || response.status === 410) setUnavailable(true);
        throw new Error(response.status === 401 ? "Your session has ended. Sign in again." : data.error || "Your reaction could not be saved. Try again.");
      }
      setReactions(data.reactions);
      setStatus(next ? `${announcementReactions.find(item => item.key === next)!.label} reaction saved.` : "Reaction removed.");
    } catch (cause) {
      setReactions(previous);
      setError(cause instanceof Error && !(cause instanceof TypeError) ? cause.message : "Your reaction could not be saved. Check your connection and try again.");
    } finally { saving.current = false; setPending(false); }
  }

  function navigate(event: KeyboardEvent<HTMLDivElement>) {
    const current = options.current.findIndex(option => option === document.activeElement);
    let next: number;
    if (event.key === "ArrowRight" || event.key === "ArrowDown") next = (current + 1) % announcementReactions.length;
    else if (event.key === "ArrowLeft" || event.key === "ArrowUp") next = (current + announcementReactions.length - 1) % announcementReactions.length;
    else if (event.key === "Home") next = 0;
    else if (event.key === "End") next = announcementReactions.length - 1;
    else { if (event.key === "Tab") { setOpen(false); trigger.current?.focus(); } return; }
    event.preventDefault();
    options.current[next]?.focus();
  }

  return <>
    <div className={styles.row} aria-label="Announcement reactions" aria-busy={pending}>
      <button ref={trigger} type="button" className={styles.trigger} aria-haspopup="menu" aria-expanded={open} aria-controls={open ? menuId : undefined}
        aria-label={open ? "Close reactions" : choice ? `Change your ${choice.label.toLowerCase()} reaction` : "Add a reaction"} disabled={unavailable} aria-disabled={pending || unavailable}
        onClick={() => { if (!saving.current) setOpen(value => !value); }} onKeyDown={event => { if (!saving.current && (event.key === "ArrowDown" || event.key === "ArrowUp")) { event.preventDefault(); setOpen(true); } }}>
        {open ? <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true"><path d="m6 6 12 12M18 6 6 18" /></svg>
          : choice ? <span className={styles.emoji} aria-hidden="true">{choice.emoji}</span>
          : <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M21 12a9 9 0 1 1-9-9M16 5h6M19 2v6M8 14.5a5 5 0 0 0 8 0" /><path d="M8.5 9h.01M14.5 9h.01" strokeWidth="2.8" /></svg>}
      </button>
      {announcementReactions.filter(item => reactions.counts[item.key] > 0).map(item => <button key={item.key} type="button" className={styles.chip}
        data-selected={reactions.choice === item.key} aria-pressed={reactions.choice === item.key} disabled={pending || unavailable}
        aria-label={`${reactions.choice === item.key ? "Remove your" : "React with"} ${item.label.toLowerCase()} reaction, ${reactions.counts[item.key]} ${reactions.counts[item.key] === 1 ? "person" : "people"}`}
        onClick={event => void react(item.key, event.currentTarget)}><span className={styles.emoji} aria-hidden="true">{item.emoji}</span>{reactions.counts[item.key]}</button>)}
      {!reactions.total ? <span className={styles.caption}>Be the first to react</span> : null}
      <span className={styles.srOnly} role="status">{status}</span>
    </div>
    {error ? <p className={styles.error} role="alert">{error}</p> : null}
    {open ? createPortal(<div ref={picker} id={menuId} role="menu" aria-label="Pick a reaction" className={styles.picker} data-side={position.side}
      style={{ top: position.top, left: position.left, "--anchor": `${position.anchor}px` } as CSSProperties} onKeyDown={navigate}>
      {announcementReactions.map((item, index) => <button key={item.key} ref={element => { options.current[index] = element; }} type="button" role="menuitemradio"
        aria-checked={reactions.choice === item.key} aria-label={item.label} tabIndex={-1} className={styles.option}
        onClick={event => void react(item.key, event.currentTarget)}><span className={styles.emoji} aria-hidden="true">{item.emoji}</span><span className={styles.optionLabel} aria-hidden="true">{item.label}</span></button>)}
    </div>, document.body) : null}
    {burst ? createPortal(<div key={burst.key} className={styles.burst} style={{ left: burst.x, top: burst.y }} aria-hidden="true">
      {[-36, 24, -12, 42, 5].map((drift, index) => <span key={index} className={styles.particle} style={{ "--drift": `${drift}px`, "--spin": `${drift / 2}deg`, "--delay": `${index * 45}ms` } as CSSProperties}>{burst.emoji}</span>)}
    </div>, document.body) : null}
  </>;
}
