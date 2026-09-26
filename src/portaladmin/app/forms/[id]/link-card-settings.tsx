"use client";

import { useId, useLayoutEffect, useRef, useState, type FormEvent } from "react";
import { createPortal } from "react-dom";
import { Cancel01Icon, Delete02Icon, Image01Icon, Link04Icon, PencilEdit02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { ErrorToast } from "@/components/ui/error-toast";
import { FormLinkCard } from "@/components/ui/form-link-card";
import { isFormLinkCard, isFormLinkUrl, type FormLinkCard as Card } from "../../../../../libs/ui/form-theme";
import { prepareLinkCardImage } from "./header-image";
import styles from "./link-card-settings.module.css";

export function LinkCardSettings({ card, onChange, disabled, onOpen, onClose }: {
  card: Card | null;
  onChange: (card: Card | null) => void;
  disabled: boolean;
  onOpen: () => void;
  onClose: () => void;
}) {
  const id = useId();
  const [draft, setDraft] = useState<Card | null>(null);
  const [preparing, setPreparing] = useState(false);
  const [error, setError] = useState<{ message: string } | null>(null);
  const dialog = useRef<HTMLDialogElement>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const headline = useRef<HTMLInputElement>(null);
  const link = useRef<HTMLInputElement>(null);
  const editing = draft !== null;

  useLayoutEffect(() => {
    if (!editing) return;
    const node = dialog.current;
    node?.showModal();
    return () => node?.close();
  }, [editing]);

  function close() {
    setDraft(null);
    setError(null);
    requestAnimationFrame(onClose);
  }

  function open() {
    setDraft(card ? { ...card } : { label: "", title: "", url: "", image: null });
    setError(null);
    onOpen();
  }

  async function chooseImage(file: File) {
    setError(null);
    setPreparing(true);
    try {
      const image = await prepareLinkCardImage(file);
      setDraft(current => current ? { ...current, image } : null);
    } catch (error) {
      setError({ message: error instanceof Error ? error.message : "This image could not be added. Try again." });
    } finally {
      setPreparing(false);
    }
  }

  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draft || disabled || preparing) return;
    const next = { ...draft, label: draft.label.trim(), title: draft.title.trim(), url: draft.url.trim() };
    if (!next.title) {
      setError({ message: "Add a headline for the link card." });
      headline.current?.focus();
      return;
    }
    if (!isFormLinkUrl(next.url)) {
      setError({ message: "Use a complete link starting with https:// or http://." });
      link.current?.focus();
      return;
    }
    if (!isFormLinkCard(next)) {
      setError({ message: "Check the card text and choose a JPG, PNG or WebP thumbnail." });
      return;
    }
    onChange(next);
    close();
  }

  return <>
    <div className={styles.settings}>
      <div className={styles.settingsHeader}>
        <div><h3>Featured link</h3><p>Share a recap, video, or website.</p></div>
        <button type="button" className={styles.add} onClick={open} disabled={disabled} aria-label={card ? "Edit link card" : "Add link card"}>
          <Icon icon={card ? PencilEdit02Icon : Link04Icon} size={15} />{card ? "Edit" : "Add card"}
        </button>
      </div>
      {card ? <div className={styles.settingsPreview}>
        <div className={styles.settingsPreviewHeading}><span>Preview</span><span>Bottom right</span></div>
        <div className={styles.settingsStage}><FormLinkCard card={card} interactive={false} /></div>
        <div className={styles.destination}><Icon icon={Link04Icon} size={14} /><span>{new URL(card.url).hostname.replace(/^www\./, "")}</span></div>
      </div> : <div className={styles.empty}>
        <Icon icon={Link04Icon} size={22} />
        <p>Your card will appear in the bottom-right corner of the form.</p>
      </div>}
    </div>
    {draft ? createPortal(<dialog ref={dialog} className={styles.modal} aria-labelledby={`${id}-heading`}
      onCancel={event => { event.preventDefault(); close(); }} onClick={event => { if (event.target === event.currentTarget) close(); }}>
      <div className={styles.modalHeader}>
        <div><h2 id={`${id}-heading`}>{card ? "Edit link card" : "Add link card"}</h2><p>Add a recap, video, or website to your form.</p></div>
        <button type="button" className={styles.close} aria-label="Close link card settings" onClick={close}><Icon icon={Cancel01Icon} size={18} /></button>
      </div>
      <form onSubmit={save} noValidate>
        <div className={styles.body}>
          <fieldset className={styles.fields} disabled={disabled || preparing}>
            <legend className={styles.srOnly}>Card details</legend>
            <div className={styles.thumbnailRow}>
              <button type="button" className={styles.thumbnailUpload} aria-label={draft.image ? "Change card thumbnail" : "Choose card thumbnail"} onClick={() => fileInput.current?.click()}>
                {draft.image ? <img src={draft.image} alt="Card thumbnail" /> : <Icon icon={Image01Icon} size={24} />}
              </button>
              <div className={styles.thumbnailCopy}>
                <strong>Thumbnail <span>Optional</span></strong>
                <span>{draft.image ? "JPG, PNG or WebP" : "Leave empty to use a preview from your video link."}</span>
                <div className={styles.thumbnailLinks}>
                  <button type="button" onClick={() => fileInput.current?.click()}>{preparing ? "Preparing…" : draft.image ? "Change image" : "Upload image"}</button>
                  {draft.image ? <button type="button" onClick={() => setDraft({ ...draft, image: null })}>Remove</button> : null}
                </div>
              </div>
            </div>
            <input ref={fileInput} className={styles.fileInput} type="file" accept="image/jpeg,image/png,image/webp" aria-label="Choose card thumbnail"
              onChange={event => { const file = event.target.files?.[0]; event.target.value = ""; if (file) void chooseImage(file); }} />
            <label className={styles.field} htmlFor={`${id}-title`}>Headline
              <input ref={headline} id={`${id}-title`} value={draft.title} maxLength={80} placeholder="Watch the recap" autoFocus
                onChange={event => { setDraft({ ...draft, title: event.target.value }); setError(null); }} />
            </label>
            <label className={styles.field} htmlFor={`${id}-label`}>Label <span>Optional</span>
              <input id={`${id}-label`} value={draft.label} maxLength={60} placeholder="Morgan Hacks 2026"
                onChange={event => { setDraft({ ...draft, label: event.target.value }); setError(null); }} />
            </label>
            <label className={styles.field} htmlFor={`${id}-url`}>Destination link
              <input ref={link} id={`${id}-url`} type="url" value={draft.url} maxLength={2048} placeholder="https://" autoCapitalize="none" spellCheck={false}
                onChange={event => { setDraft({ ...draft, url: event.target.value }); setError(null); }} />
            </label>
          </fieldset>
          <div className={styles.preview}>
            <div className={styles.previewHeading}><span>Preview</span><span>Bottom right</span></div>
            <div className={styles.stage}><FormLinkCard card={{ ...draft, title: draft.title || "Your headline" }} interactive={false} /></div>
          </div>
        </div>
        <div className={styles.actions}>
          {card ? <button type="button" className={styles.remove} disabled={disabled || preparing} onClick={() => { onChange(null); close(); }}><Icon icon={Delete02Icon} size={16} />Remove card</button> : null}
          <button type="button" className={styles.cancel} onClick={close}>Cancel</button>
          <button type="submit" className={styles.save} disabled={disabled || preparing}>{card ? "Save card" : "Add card"}</button>
        </div>
      </form>
      <ErrorToast title="Check your link card" message={error?.message} revision={error} />
      <span className={styles.srOnly} role="status">{preparing ? "Preparing card thumbnail" : ""}</span>
    </dialog>, document.body) : null}
  </>;
}
