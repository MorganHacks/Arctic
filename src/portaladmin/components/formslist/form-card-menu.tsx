"use client";

import { useEffect, useId, useLayoutEffect, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Cancel01Icon, Copy01Icon, Delete02Icon, MoreVerticalIcon, PencilEdit02Icon, Tick02Icon, ViewOffSlashIcon } from "@hugeicons/core-free-icons";
import { renameForm, removeForm, unpublishForm } from "@/app/forms/actions";
import { Icon } from "@/components/ui/icon";
import type { FormRow } from "@/lib/api";
import { publicLink, useCopy } from "./share-link";
import styles from "./forms-page.module.css";

type Action = "rename" | "unpublish" | "remove";

export function FormCardMenu({ form, canManage }: { form: FormRow; canManage: boolean }) {
  const id = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const menu = useRef<HTMLDivElement>(null);
  const focusLast = useRef(false);
  const [open, setOpen] = useState(false);
  const [action, setAction] = useState<Action | null>(null);
  const { state, copy } = useCopy(form.code);

  useLayoutEffect(() => {
    if (!open) return;
    function position() {
      if (!menu.current || !trigger.current) return;
      const rect = trigger.current.getBoundingClientRect();
      const viewport = window.visualViewport;
      const left = viewport?.offsetLeft ?? 0;
      const top = viewport?.offsetTop ?? 0;
      const width = viewport?.width ?? window.innerWidth;
      const height = viewport?.height ?? window.innerHeight;
      const panel = menu.current;
      panel.style.left = `${Math.max(left + 12, Math.min(rect.right - panel.offsetWidth, left + width - panel.offsetWidth - 12))}px`;
      panel.style.top = `${Math.max(top + 12, rect.bottom + panel.offsetHeight + 8 < top + height ? rect.bottom + 6 : rect.top - panel.offsetHeight - 6)}px`;
    }
    position();
    const items = menu.current?.querySelectorAll<HTMLButtonElement>("[role=menuitem]:not(:disabled)");
    items?.[focusLast.current ? items.length - 1 : 0]?.focus({ preventScroll: true });
    window.addEventListener("resize", position);
    window.addEventListener("scroll", position, true);
    window.visualViewport?.addEventListener("resize", position);
    return () => {
      window.removeEventListener("resize", position);
      window.removeEventListener("scroll", position, true);
      window.visualViewport?.removeEventListener("resize", position);
    };
  }, [open]);

  function close() { menu.current?.hidePopover(); trigger.current?.focus(); }
  function choose(next: Action) { menu.current?.hidePopover(); setAction(next); }

  return <>
    <button ref={trigger} type="button" className={styles.cardMenuTrigger} aria-label={`Actions for ${form.name}`}
      title="Form actions" aria-haspopup="menu" aria-expanded={open} aria-controls={id} popoverTarget={id}
      onClick={() => { focusLast.current = false; }}
      onKeyDown={event => {
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
          event.preventDefault(); focusLast.current = event.key === "ArrowUp"; menu.current?.showPopover();
        }
      }}><Icon icon={MoreVerticalIcon} size={20} /></button>
    <div ref={menu} id={id} role="menu" aria-label={`Actions for ${form.name}`} popover="auto" className={styles.cardMenu}
      onToggle={event => setOpen(event.newState === "open")}
      onBlurCapture={event => {
        if (event.relatedTarget && !event.currentTarget.contains(event.relatedTarget) && event.relatedTarget !== trigger.current) menu.current?.hidePopover();
      }}
      onKeyDown={event => {
        if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close(); }
        if (event.key === "Tab") close();
        if (["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
          event.preventDefault();
          const items = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>("[role=menuitem]:not(:disabled)"));
          const index = items.indexOf(document.activeElement as HTMLButtonElement);
          const next = event.key === "Home" ? 0 : event.key === "End" ? items.length - 1
            : (index + (event.key === "ArrowDown" ? 1 : -1) + items.length) % items.length;
          items[next]?.focus();
        }
      }}>
      {canManage ? <button type="button" role="menuitem" tabIndex={-1} onClick={() => choose("rename")}><Icon icon={PencilEdit02Icon} size={18} />Rename</button> : null}
      <button type="button" role="menuitem" tabIndex={-1} onClick={copy}><Icon icon={state === "copied" ? Tick02Icon : Copy01Icon} size={18} />{state === "copied" ? "Link copied" : "Copy link"}</button>
      {canManage ? <>
        <button type="button" role="menuitem" tabIndex={-1} disabled={!form.published} title={!form.published ? "This form is already unpublished" : undefined} onClick={() => choose("unpublish")}><Icon icon={ViewOffSlashIcon} size={18} />Unpublish</button>
        <div className={styles.menuDivider} role="separator" />
        <button type="button" role="menuitem" tabIndex={-1} className={styles.menuDanger} onClick={() => choose("remove")}><Icon icon={Delete02Icon} size={18} />Remove</button>
      </> : null}
      <span role="status" className={state === "failed" ? styles.menuCopyError : styles.menuStatus}>
        {state === "copied" ? "Link copied to clipboard." : state === "failed" ? `Could not copy. ${publicLink(form.code)}` : null}
      </span>
    </div>
    {action ? <FormActionDialog form={form} action={action} onClose={() => { setAction(null); trigger.current?.focus(); }} /> : null}
  </>;
}

function FormActionDialog({ form, action, onClose }: { form: FormRow; action: Action; onClose: () => void }) {
  const router = useRouter();
  const id = useId();
  const dialog = useRef<HTMLDialogElement>(null);
  const input = useRef<HTMLInputElement>(null);
  const cancel = useRef<HTMLButtonElement>(null);
  const [name, setName] = useState(form.name);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();
  const renaming = action === "rename";
  const title = renaming ? "Rename form" : action === "remove" ? "Remove form?" : "Unpublish form?";

  useEffect(() => {
    dialog.current?.showModal();
    if (renaming) { input.current?.focus(); input.current?.select(); }
    else cancel.current?.focus();
  }, [renaming]);

  return <dialog ref={dialog} className={styles.dialog} aria-labelledby={`${id}-title`} aria-describedby={`${id}-description`}
    onClose={onClose} onCancel={event => { if (pending) event.preventDefault(); }}>
    <form onSubmit={event => {
      event.preventDefault();
      if (pending || renaming && (!name.trim() || name.trim() === form.name)) return;
      setError(null);
      startTransition(async () => {
        try {
          const result = renaming ? await renameForm(form.id, name) : action === "remove" ? await removeForm(form.id) : await unpublishForm(form.id);
          if (!result.ok) { setError(result.error ?? "That did not work. Try again."); return; }
          dialog.current?.close(); router.refresh();
        } catch { setError("That did not work. Try again."); }
      });
    }}>
      <div className={styles.dialogHeader}><h2 id={`${id}-title`}>{title}</h2><button type="button" className={styles.closeButton} aria-label="Close dialog" disabled={pending} onClick={() => dialog.current?.close()}><Icon icon={Cancel01Icon} size={20} /></button></div>
      <p id={`${id}-description`} className={styles.dialogDescription}>
        {renaming ? "Give this form a new name. Its share link stays the same."
          : action === "remove" ? <><strong>{form.name}</strong> will leave your forms list and its public link will stop working. Responses and version history will be retained.</>
            : <><strong>{form.name}</strong> will stop accepting responses. Existing responses will be kept, and you can publish it again from the editor.</>}
      </p>
      {renaming ? <div className={styles.field}><label htmlFor={`${id}-name`}>Form name</label><input ref={input} id={`${id}-name`} required maxLength={200} autoComplete="off" value={name} disabled={pending} onChange={event => setName(event.target.value)} /></div> : null}
      {error ? <p role="alert" className={styles.error}>{error}</p> : null}
      <div className={styles.dialogActions}>
        <button ref={cancel} type="button" className={styles.secondaryButton} disabled={pending} onClick={() => dialog.current?.close()}>Cancel</button>
        <button type="submit" className={`${styles.primaryButton} ${action === "remove" ? styles.removeButton : ""}`} disabled={pending || renaming && (!name.trim() || name.trim() === form.name)}>
          {pending ? renaming ? "Saving…" : action === "remove" ? "Removing…" : "Unpublishing…" : renaming ? "Save name" : action === "remove" ? "Remove form" : "Unpublish"}
        </button>
      </div>
    </form>
  </dialog>;
}
