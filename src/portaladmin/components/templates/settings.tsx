import { ErrorToast } from "@/components/ui/error-toast";
import Image from "next/image";
import { useEffect, useRef, useState } from "react";
import { ArrowRight02Icon, BulbIcon, CheckmarkCircle02Icon, InboxIcon, Link01Icon, SourceCodeIcon, TextBoldIcon, TextFontIcon, TextItalicIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { PersonalizedField } from "./personalized-field";
import { FROM_DOMAIN, FROM_LOCAL, type DraftHandle } from "./use-draft";
import type { Placeholder, TemplateFormat } from "./types";
import type { TemplateFieldErrors } from "./validation";
import styles from "./settings.module.css";

export function TemplateSettings({
  handle,
  available,
  errors,
}: {
  handle: DraftHandle;
  available: Placeholder[] | null;
  errors: TemplateFieldErrors;
}) {
  const { draft, set } = handle;
  const [editingReplyTo, setEditingReplyTo] = useState(false);
  const replyToInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (editingReplyTo && !errors.name && !errors.subject && !errors.previewText && !errors.fromName) {
      replyToInput.current?.focus();
    }
  }, [editingReplyTo]);

  useEffect(() => {
    if (errors.replyTo) setEditingReplyTo(true);
  }, [errors.replyTo]);

  return (
    <div className={styles.sections}>
      <section aria-labelledby="template-details-title">
        <div className={styles.sectionHeading}>
          <h2 id="template-details-title">Details</h2>
          <p>The essentials for your email template.</p>
        </div>
        <div className={styles.fields}>
          <div className={styles.field}>
            <label htmlFor="template-name">Campaign name <span className={styles.optional}>Optional</span></label>
            <input
              id="template-name"
              value={draft.name}
              onChange={(event) => set("name", event.target.value)}
              autoComplete="off"
              maxLength={200}
              placeholder="e.g. Event recap"
              aria-invalid={Boolean(errors.name) || undefined}
              aria-describedby={errors.name ? "template-name-error" : "template-name-help"}
              className={styles.input}
            />
            {errors.name ? <ErrorToast descriptionId="template-name-error" message={errors.name} /> : (
              <p id="template-name-help" className={styles.help}>Leave blank to use the email subject.</p>
            )}
          </div>
          <div className={styles.field}>
            <label htmlFor="subject">Email subject</label>
            <PersonalizedField
              id="subject"
              label="Email subject"
              placeholder="What’s this email about?"
              describedBy={errors.subject ? "template-subject-error" : available?.length ? "template-subject-help" : undefined}
              value={draft.subject}
              onChange={(value) => set("subject", value)}
              available={available}
              className={styles.input}
              validationError={errors.subject}
            />
            {errors.subject ? <ErrorToast descriptionId="template-subject-error" message={errors.subject} /> : null}
            {!errors.subject && available && available.length > 0 ? (
              <p id="template-subject-help" className={styles.help}>Type {"{{"} to add a personalized field.</p>
            ) : null}
          </div>
          <div className={styles.field}>
            <label htmlFor="previewText">Email preview text <span className={styles.optional}>Optional</span></label>
            <PersonalizedField
              id="previewText"
              label="Email preview text"
              value={draft.previewText}
              onChange={(value) => set("previewText", value)}
              available={available}
              placeholder="A little more reason to open your email"
              validationError={errors.previewText}
              describedBy={errors.previewText ? "template-preview-error" : "template-preview-tip"}
              className={styles.input}
            />
            {errors.previewText ? <ErrorToast descriptionId="template-preview-error" message={errors.previewText} /> : null}
            <p id="template-preview-tip" className={styles.tip}>
              <Icon icon={BulbIcon} size={17} />
              <span><strong>Tip:</strong> We recommend adding email preview text.</span>
            </p>
          </div>
        </div>
      </section>

      <section className={styles.senderSection} aria-labelledby="template-sender-title">
        <div className={`${styles.sectionHeading} ${styles.senderHeading}`}>
          <h2 id="template-sender-title">Sender</h2>
          <button
            type="button"
            className={styles.senderEdit}
            aria-expanded={editingReplyTo}
            aria-controls="reply-to-editor"
            onClick={() => setEditingReplyTo((open) => !open)}
          >
            {editingReplyTo ? "Hide reply-to address" : "Edit reply-to address"}
          </button>
        </div>
        <div className={styles.fields}>
          <div className={styles.senderRow}>
            <input
              id="fromName"
              aria-label="Sender name"
              value={draft.fromName}
              onChange={(event) => set("fromName", event.target.value)}
              autoComplete="off"
              maxLength={64}
              placeholder="MorganHacks"
              aria-invalid={Boolean(errors.fromName) || undefined}
              aria-describedby={errors.fromName ? "template-sender-error" : undefined}
              className={styles.input}
            />
            <input
              id="sender-address"
              aria-label="Sender email"
              value={`${FROM_LOCAL}@${FROM_DOMAIN}`}
              readOnly
              className={styles.input}
            />
          </div>
          {errors.fromName ? <ErrorToast descriptionId="template-sender-error" message={errors.fromName} /> : null}
          <div id="reply-to-editor" className={styles.field} hidden={!editingReplyTo}>
            <label htmlFor="replyTo">Reply-to email <span className={styles.optional}>Optional</span></label>
            <input
              id="replyTo"
              ref={replyToInput}
              type="email"
              value={draft.replyTo}
              onChange={(event) => set("replyTo", event.target.value)}
              autoComplete="off"
              spellCheck={false}
              placeholder="hello@morganhacks.com"
              aria-invalid={Boolean(errors.replyTo) || undefined}
              aria-describedby={errors.replyTo ? "template-reply-error" : "template-reply-help"}
              className={styles.input}
            />
            {errors.replyTo ? <ErrorToast descriptionId="template-reply-error" message={errors.replyTo} /> : (
              <p id="template-reply-help" className={styles.help}>Replies will go here. Leave empty to use the sender email.</p>
            )}
          </div>
        </div>
      </section>

      <label className={styles.tracking} htmlFor="click-tracking">
        <span className={styles.trackingCopy}>
          <span id="click-tracking-label" className={styles.trackingTitle}>Enable click tracking</span>
          <span id="click-tracking-help" className={styles.trackingDescription}>Track link clicks for analytics and insights.</span>
        </span>
        <input
          id="click-tracking"
          type="checkbox"
          role="switch"
          checked={draft.clickTracking}
          onChange={(event) => set("clickTracking", event.target.checked)}
          aria-labelledby="click-tracking-label"
          aria-describedby="click-tracking-help"
          className={styles.trackingSwitch}
        />
      </label>
    </div>
  );
}

export function InboxPreview({ sender, subject, snippet }: { sender: string; subject: string; snippet: string }) {
  return (
    <section aria-labelledby="inbox-preview-title">
      <div className={styles.sectionHeading}>
        <h2 id="inbox-preview-title">Inbox preview</h2>
        <p>A first look at what your recipients will see.</p>
      </div>
      <div className={styles.inbox}>
        <div className={styles.inboxToolbar}>
          <span><Icon icon={InboxIcon} size={18} /> Inbox</span>
          <span className={styles.previewLabel}>Preview</span>
        </div>
        <div className={styles.message}>
          <div className={styles.messageAvatar}><Image src="/brands/gmail.svg" alt="Gmail" width={24} height={24} /></div>
          <div className={styles.messageContent}>
            <div className={styles.messageTop}>
              <span className={styles.messageSender}>{sender.trim() || `${FROM_LOCAL}@${FROM_DOMAIN}`}</span>
              <span className={styles.messageTime}>Now</span>
            </div>
            <p className={styles.messageSubject}>{subject.trim() || "Your email subject"}</p>
            <p className={styles.messageSnippet}>{snippet.replace(/\s+/g, " ").trim() || "Your message preview will appear here."}</p>
          </div>
        </div>
        <div className={styles.otherMessages} aria-hidden="true">
          {[0, 1].map((row) => (
            <div className={styles.otherMessage} key={row}>
              <span className={styles.otherAvatar} />
              <div><span className={styles.senderLine} /><span className={styles.subjectLine} /></div>
              <span className={styles.timeLine} />
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

const formats = [
  { value: "markdown", name: "Markdown", description: "Write a simple, polished email with easy text formatting." },
  { value: "html", name: "Custom HTML", description: "Build your own email layout with full control over the code." },
] satisfies { value: TemplateFormat; name: string; description: string }[];

function FormatPreview({ format }: { format: TemplateFormat }) {
  return (
    <span className={styles.formatPreview} aria-hidden="true">
      {format === "markdown" ? (
        <>
          <span className={styles.formatDocument}>
            <span className={styles.documentHeading} />
            <span className={styles.documentLine} />
            <span className={styles.documentLine} />
            <span className={styles.documentShortLine} />
            <span className={styles.documentButton} />
          </span>
          <span className={styles.formatToolbar}>
            <span className={styles.toolbarText}><Icon icon={TextFontIcon} size={14} /> Text</span>
            <span className={styles.toolbarDivider} />
            <Icon icon={TextBoldIcon} size={13} />
            <Icon icon={TextItalicIcon} size={13} />
            <Icon icon={Link01Icon} size={13} />
          </span>
        </>
      ) : (
        <span className={styles.codeWindow}>
          <span className={styles.codeToolbar}>
            <span className={styles.windowDots}><i /><i /><i /></span>
            <span>HTML</span>
            <Icon icon={SourceCodeIcon} size={13} />
          </span>
          <span className={styles.codeLines}>
            {['<table role="presentation">', "  <tr>", "    <td>", "      Hello, MorganHacks", "    </td>", "  </tr>", "</table>"].map((line, index) => {
              const tag = line.match(/^(\s*)(<\/?)(\w+)(?: ([\w-]+)="([^"]*)")?(>)$/);
              return (
                <span className={styles.codeLine} key={index}>
                  <span className={styles.lineNumber}>{index + 1}</span>
                  {tag ? (
                    <span>
                      {tag[1]}
                      <span className={styles.codePunctuation}>{tag[2]}</span>
                      <span className={styles.codeTag}>{tag[3]}</span>
                      {tag[4] ? (
                        <>
                          {" "}<span className={styles.codeAttribute}>{tag[4]}</span>
                          <span className={styles.codePunctuation}>=</span>
                          <span className={styles.codeString}>{`"${tag[5]}"`}</span>
                        </>
                      ) : null}
                      <span className={styles.codePunctuation}>{tag[6]}</span>
                    </span>
                  ) : <span className={styles.codeText}>{line}</span>}
                </span>
              );
            })}
          </span>
        </span>
      )}
    </span>
  );
}

export function FormatPicker({ handle }: { handle: DraftHandle }) {
  return (
    <section aria-labelledby="email-format-title">
      <div className={styles.sectionHeading}>
        <h2 id="email-format-title">Email format</h2>
        <p>Choose how you want to write your email.</p>
      </div>
      <div className={styles.formatOptions} role="group" aria-label="Email format">
        {formats.map((format) => (
          <button
            key={format.value}
            type="button"
            className={styles.formatOption}
            aria-pressed={handle.draft.format === format.value}
            aria-labelledby={`format-${format.value}-title`}
            aria-describedby={`format-${format.value}-description`}
            onClick={() => handle.set("format", format.value)}
          >
            <FormatPreview format={format.value} />
            {handle.draft.format === format.value ? (
              <span className={styles.formatSelected}><Icon icon={CheckmarkCircle02Icon} size={18} /></span>
            ) : null}
            <span className={styles.formatBody}>
              <span id={`format-${format.value}-title`} className={styles.formatName}>{format.name}</span>
              <span id={`format-${format.value}-description`} className={styles.formatDescription}>{format.description}</span>
              <span className={styles.formatArrow}><Icon icon={ArrowRight02Icon} size={19} /></span>
            </span>
          </button>
        ))}
      </div>
      <p className={styles.help}>Changing the format keeps your current content.</p>
    </section>
  );
}
