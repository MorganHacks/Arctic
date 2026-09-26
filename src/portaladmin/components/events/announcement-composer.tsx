"use client";

import { useEffect, useId, useRef, useState } from "react";
import { Image01Icon, ImagesIcon, ChartBarIncreasingIcon, CheckmarkSquare01Icon, Video01Icon, Cancel01Icon, Add01Icon, ArrowDown01Icon, Clock01Icon } from "@hugeicons/core-free-icons";
import { DateTimePicker } from "@/components/ui/date-time-picker";
import { Icon } from "@/components/ui/icon";
import { announcementLabels, announcementError, emptyAnnouncementContent, mediaUrl, videoEmbed, type AnnouncementContent, type AnnouncementKind } from "../../../../libs/ui/announcements";
import { fromLocalInput, readable, toLocalInput } from "./zone";
import styles from "./announcement-composer.module.css";

const tools = [
  { kind: "image", icon: Image01Icon },
  { kind: "imagePoll", icon: ImagesIcon },
  { kind: "poll", icon: ChartBarIncreasingIcon },
  { kind: "quiz", icon: CheckmarkSquare01Icon },
  { kind: "video", icon: Video01Icon },
] as const;

export function AnnouncementComposer({ body, onBody, content, onContent, pending, onPost }: {
  body: string; onBody: (value: string) => void;
  content: AnnouncementContent | null; onContent: (value: AnnouncementContent | null) => void;
  pending: boolean; onPost: (publishAt: string | null) => void;
}) {
  const drafts = useRef<Partial<Record<AnnouncementKind, AnnouncementContent>>>({});
  const [menuOpen, setMenuOpen] = useState(false);
  const [scheduledTime, setScheduledTime] = useState<string | null>(null);
  const menuId = useId();
  const splitRef = useRef<HTMLDivElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const menuItemRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!menuOpen) return;
    menuItemRef.current?.focus();
    function outside(event: PointerEvent) {
      if (!splitRef.current?.contains(event.target as Node)) setMenuOpen(false);
    }
    document.addEventListener("pointerdown", outside);
    return () => document.removeEventListener("pointerdown", outside);
  }, [menuOpen]);
  const publishAt = scheduledTime === null ? null : fromLocalInput(scheduledTime);
  const scheduleIssue = scheduledTime !== null && (!publishAt || toLocalInput(publishAt) !== scheduledTime || Date.parse(publishAt) <= Date.now());
  const left = 500 - body.length;
  const issue = announcementError(content);
  const cannotPost = pending || !body.trim() || left < 0 || !!issue;
  function schedule() {
    setMenuOpen(false);
    setScheduledTime(toLocalInput(new Date(Date.now() + 60 * 60 * 1000).toISOString()));

  }
  function choose(kind: AnnouncementKind) {
    if (content) drafts.current[content.kind] = content;
    onContent(drafts.current[kind] ?? emptyAnnouncementContent(kind));
  }
  function updateOption(index: number, values: { text?: string; imageUrl?: string }) {
    if (!content) return;
    onContent({ ...content, options: content.options?.map((option, i) => i === index ? { ...option, ...values } : option) });
  }
  function removeOption(index: number) {
    if (!content) return;
    const correct = content.correctOption;
    onContent({ ...content, options: content.options?.filter((_, i) => i !== index),
      correctOption: correct == null || correct === index ? null : correct > index ? correct - 1 : correct });
  }
  return (
    <div className={styles.write}>
      <label htmlFor="notice" className={styles.label}>{content?.kind === "quiz" || content?.kind === "poll" || content?.kind === "imagePoll" ? "Ask your event a question" : "What would you like to share?"}</label>
      <div className={styles.composer}>
        <textarea id="notice" className={styles.text} value={body} onChange={event => onBody(event.target.value)}
          rows={content ? 2 : 4} disabled={pending} aria-describedby="notice-warning notice-count"
          placeholder={content?.options ? "Ask a question…" : "Share an update with your event…"} />
        {content && (
          <fieldset className={styles.attachment} disabled={pending}>
            <legend>{announcementLabels[content.kind]}</legend>
            <button type="button" className={styles.removeAttachment} aria-label="Remove attachment" onClick={() => onContent(null)}><Icon icon={Cancel01Icon} size={18} /></button>
            {content.media?.map((item, index) => (
              <div className={styles.mediaRow} key={index}>
                {content.kind === "image" && mediaUrl(item.url) ? <img className={styles.thumbnail} src={item.url} alt={item.alt || "Image preview"} referrerPolicy="no-referrer" /> : null}
                <div className={styles.mediaInputs}>
                  <label>{content.kind === "video" ? "Video URL" : `Image ${index + 1} URL`}
                    <input type="url" value={item.url} maxLength={2048} placeholder={content.kind === "video" ? "https://youtube.com/watch?v=…" : "https://…/image.jpg"}
                      onChange={event => onContent({ ...content, media: content.media?.map((media, i) => i === index ? { ...media, url: event.target.value } : media) })} />
                  </label>
                  {content.kind === "image" ? <label>Image description <span className={styles.optional}>Optional</span>
                    <input value={item.alt ?? ""} maxLength={200} placeholder="Describe this image for people using screen readers"
                      onChange={event => onContent({ ...content, media: content.media?.map((media, i) => i === index ? { ...media, alt: event.target.value } : media) })} />
                  </label> : <p className={styles.hint}>YouTube, Vimeo, or a direct link to a video file.</p>}
                </div>
                {(content.media?.length ?? 0) > 1 ? <button type="button" className={styles.remove} aria-label={`Remove image ${index + 1}`} onClick={() => onContent({ ...content, media: content.media?.filter((_, i) => i !== index) })}><Icon icon={Cancel01Icon} size={16} /></button> : null}
              </div>
            ))}
            {content.kind === "video" && mediaUrl(content.media?.[0]?.url ?? "") ? (
              <div className={styles.videoPreview}>
                {videoEmbed(content.media![0].url) ? <iframe src={videoEmbed(content.media![0].url)!} title="Video preview" allowFullScreen referrerPolicy="strict-origin-when-cross-origin" /> : <video controls preload="metadata" src={content.media![0].url} />}
              </div>
            ) : null}
            {content.kind === "image" && (content.media?.length ?? 0) < 4 ? <button type="button" className={styles.add} onClick={() => onContent({ ...content, media: [...(content.media ?? []), { url: "", alt: "" }] })}><Icon icon={Add01Icon} size={16} />Add image</button> : null}
            {content.options ? (
              <div className={styles.options}>
                {content.options.map((option, index) => (
                  <div className={styles.option} key={index}>
                    <span className={styles.optionNumber}>{String.fromCharCode(65 + index)}</span>
                    {content.kind === "imagePoll" && mediaUrl(option.imageUrl ?? "") ? <img className={styles.optionImage} src={option.imageUrl!} alt={option.text || `Choice ${index + 1}`} referrerPolicy="no-referrer" /> : null}
                    <div className={styles.optionInputs}>
                      <input aria-label={`Choice ${index + 1}`} value={option.text} maxLength={100} placeholder={`Choice ${index + 1}`} onChange={event => updateOption(index, { text: event.target.value })} />
                      {content.kind === "imagePoll" ? <input aria-label={`Choice ${index + 1} image URL`} type="url" maxLength={2048} value={option.imageUrl ?? ""} placeholder="Image URL · https://…" onChange={event => updateOption(index, { imageUrl: event.target.value })} /> : null}
                    </div>
                    {content.kind === "quiz" ? <label className={styles.correct}><input type="radio" name="correct-answer" checked={content.correctOption === index} onChange={() => onContent({ ...content, correctOption: index })} aria-label={`Mark choice ${index + 1} as correct`} /><span>Correct</span></label> : null}
                    {content.options!.length > 2 ? <button type="button" className={styles.remove} aria-label={`Remove choice ${index + 1}`} onClick={() => removeOption(index)}><Icon icon={Cancel01Icon} size={16} /></button> : null}
                  </div>
                ))}
                {content.options.length < 4 ? <button type="button" className={styles.add} onClick={() => onContent({ ...content, options: [...content.options!, { text: "", ...(content.kind === "imagePoll" ? { imageUrl: "" } : {}) }] })}><Icon icon={Add01Icon} size={16} />Add choice</button> : null}
                {content.kind === "quiz" ? <label className={styles.explanation}>Answer explanation <span className={styles.optional}>Optional</span><input value={content.explanation ?? ""} maxLength={280} placeholder="Shown after someone answers" onChange={event => onContent({ ...content, explanation: event.target.value })} /></label> : <p className={styles.hint}>One vote per person. Results appear after voting.</p>}
              </div>
            ) : null}
            {issue ? <p className={styles.hint} aria-live="polite">{issue}</p> : null}
          </fieldset>
        )}
        {scheduledTime !== null ? <div className={styles.schedule}>
          <div className={styles.scheduleHeading}><Icon icon={Clock01Icon} size={18} /><strong>Schedule post</strong><button type="button" disabled={pending} onClick={() => setScheduledTime(null)}>Cancel</button></div>
          <label htmlFor={`${menuId}-time`}>Date and time <span>Eastern Time</span></label>
          <DateTimePicker id={`${menuId}-time`} label="Post date and time" value={scheduledTime} disabled={pending}
            min={toLocalInput(new Date(Date.now() + 60000).toISOString())} onChange={setScheduledTime} describedBy={`${menuId}-schedule-hint`} />
          <p id={`${menuId}-schedule-hint`} className={styles.hint}>{scheduleIssue ? "Choose a future date and time." : `Appears in the applicant feed ${readable(publishAt)}.`}</p>
        </div> : null}
        <div className={styles.footer}>
          <div className={styles.toolbar} role="group" aria-label="Add to announcement">
            {tools.map(tool => <button key={tool.kind} type="button" className={styles.tool} aria-pressed={content?.kind === tool.kind} disabled={pending} onClick={() => choose(tool.kind)}><Icon icon={tool.icon} size={20} strokeWidth={1.7} /><span>{announcementLabels[tool.kind]}</span></button>)}
          </div>
          <div className={styles.send}>
            <span id="notice-count" className={left < 0 ? styles.over : styles.count}>{left < 0 ? `${-left} over` : `${left} left`}</span>
            <div ref={splitRef} className={styles.splitWrap} onKeyDown={event => {
              if (event.key === "Escape" && menuOpen) { event.preventDefault(); event.stopPropagation(); setMenuOpen(false); toggleRef.current?.focus(); }
            }}>
              <div className={styles.split} data-disabled={cannotPost || scheduleIssue}>
                <button type="button" className={styles.post} disabled={cannotPost || scheduleIssue} onClick={() => onPost(publishAt)}>{pending ? scheduledTime === null ? "Posting…" : "Scheduling…" : scheduledTime === null ? "Post" : "Schedule"}</button>
                <button ref={toggleRef} type="button" className={styles.chevron} aria-label="Post options" aria-haspopup="menu" aria-expanded={menuOpen} aria-controls={menuId}
                  disabled={cannotPost} onClick={() => setMenuOpen(open => !open)} onKeyDown={event => {
                    if (event.key === "ArrowDown" || event.key === "ArrowUp") { event.preventDefault(); setMenuOpen(true); }
                  }}><Icon icon={ArrowDown01Icon} size={20} strokeWidth={2} /></button>
              </div>
              {menuOpen ? <div className={styles.postMenu} id={menuId} role="menu" aria-label="Post options">
                <button type="button" ref={menuItemRef} role="menuitem" onClick={scheduledTime === null ? schedule : () => { setScheduledTime(null); setMenuOpen(false); toggleRef.current?.focus(); }}
                  onKeyDown={event => { if (event.key === "Tab") setMenuOpen(false); if (event.key === "ArrowDown" || event.key === "ArrowUp") event.preventDefault(); }}>
                  <Icon icon={Clock01Icon} size={20} />{scheduledTime === null ? "Schedule post" : "Post now instead"}
                </button>
              </div> : null}
            </div>
          </div>
        </div>
      </div>
      <p id="notice-warning" className={styles.warning}>Visible to everyone at this event. Posts can be taken down, but cannot be edited.</p>
    </div>
  );
}
