"use client";

import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState, useTransition } from "react";
import { addTemplate, editTemplate, previewBody } from "@/app/templates/actions";
import { EmailPreview } from "./email-preview";
import styles from "./templates.module.css";
import {
  placeholdersIn,
  type Rendered,
  type Template,
  type TemplateDraft,
  type TemplateKind,
} from "./types";

/** How long to wait after the last keystroke before rendering. */
const DEBOUNCE_MS = 600;

/**
 * Writing an email.
 *
 * One component for both the new template and the existing one, because they
 * are the same screen with one difference: the key. On a new template it is a
 * field, and on an existing one it is the address the save is written to and
 * cannot move — there is no rename, and a key that drifted would create a
 * second template while looking like it was editing the first.
 *
 * The preview is the API's. Everything typed on the left is sent to the render
 * endpoint after a pause and comes back as the html and text a send would
 * build. There is no markdown in this file on purpose: two renderers agree
 * until the day somebody types the thing they disagree about, and the one that
 * matters is the one that sends.
 */
export function Editor({
  template,
  canManage,
}: {
  template: Template | null;
  canManage: boolean;
}) {
  const router = useRouter();

  const [key, setKey] = useState(template?.key ?? "");
  const [kind, setKind] = useState<TemplateKind>(template?.kind ?? "broadcast");
  const [subject, setSubject] = useState(template?.subject ?? "");
  const [fromLocal, setFromLocal] = useState(template?.fromLocal ?? "");
  const [fromDomain, setFromDomain] = useState(template?.fromDomain ?? "");
  const [replyTo, setReplyTo] = useState(template?.replyTo ?? "");
  const [markdown, setMarkdown] = useState(template?.markdown ?? "");

  /*
   * Seeded from what the API already rendered.
   *
   * An existing template arrives with its html and text on it, so the message
   * on the right is drawn on the first paint rather than after a round trip
   * that would render exactly what the server already sent.
   */
  const [rendered, setRendered] = useState<Rendered | null>(
    template
      ? { subject: template.subject, html: template.html, text: template.text }
      : null,
  );

  const [renderError, setRenderError] = useState<string | null>(null);
  const [rendering, setRendering] = useState(false);
  const [asked, setAsked] = useState(false);
  const [outcome, setOutcome] = useState<{ ok: boolean; text: string } | null>(
    null,
  );
  const [saving, startSaving] = useTransition();

  /**
   * The render this component is waiting on.
   *
   * Debouncing makes overlapping renders rare rather than impossible. A slow
   * one and a fast one started after it can land out of order, and the older
   * answer would then be the email on screen. Only the newest may speak.
   */
  const attempt = useRef(0);

  /** The names the body asks for, derived from what is typed rather than stored. */
  const placeholders = useMemo(
    () => placeholdersIn(subject, markdown),
    [subject, markdown],
  );

  useEffect(() => {
    if (markdown.trim() === "" && subject.trim() === "") {
      setRendered(null);
      setRenderError(null);
      return;
    }

    const timer = setTimeout(() => {
      const mine = (attempt.current += 1);
      setRendering(true);

      void previewBody({ subject, markdown }).then((result) => {
        if (mine !== attempt.current) {
          return;
        }

        setRendering(false);

        if (result.ok) {
          setRendered(result.rendered);
          setRenderError(null);
        } else {
          setRenderError(result.error);
        }
      });
    }, DEBOUNCE_MS);

    return () => clearTimeout(timer);
  }, [subject, markdown]);

  function draft(): TemplateDraft {
    return {
      key,
      kind,
      subject,
      markdown,
      fromLocal,
      fromDomain,
      replyTo: replyTo.trim() === "" ? null : replyTo,
    };
  }

  function save() {
    setOutcome(null);

    startSaving(async () => {
      const result = template
        ? await editTemplate(template.key, draft())
        : await addTemplate(draft());

      setAsked(false);

      if (!result.ok) {
        setOutcome({ ok: false, text: result.error });
        return;
      }

      // A template that has just been created is opened, because everything
      // after this point — the version, the key it answers to — belongs to the
      // page that edits it.
      if (!template) {
        router.push(`/templates/${encodeURIComponent(result.key)}`);
        router.refresh();
        return;
      }

      setOutcome({
        ok: true,
        text: [
          result.version === null
            ? "Saved."
            : `Saved. Now version ${result.version}.`,
          result.note,
        ]
          .filter(Boolean)
          .join(" "),
      });
      router.refresh();
    });
  }

  return (
    <div className={styles.editor}>
      <div>
        <fieldset className={styles.form} disabled={!canManage}>
          <div className={styles.field}>
            <label htmlFor="key">Key</label>
            {template ? (
              <p className="mono" style={{ margin: 0 }}>
                {template.key}
              </p>
            ) : (
              <>
                <input
                  id="key"
                  value={key}
                  onChange={(event) => setKey(event.target.value)}
                  autoComplete="off"
                  spellCheck={false}
                  className={styles.wide}
                />
                <p className={styles.medium}>A key cannot be changed later.</p>
              </>
            )}
          </div>

          <div className={styles.field}>
            <label htmlFor="kind">Kind</label>
            <select
              id="kind"
              value={kind}
              onChange={(event) =>
                setKind(event.target.value as TemplateKind)
              }
            >
              <option value="broadcast">Broadcast</option>
              <option value="transactional">Transactional</option>
            </select>
            {/* Said here because this is where the choice is made, and the
                compose screen simply will not list a transactional template —
                which reads as a missing template rather than as a decision
                somebody took on this page. */}
            <p className={styles.medium}>
              Campaigns can only send broadcast templates.
            </p>
          </div>

          <div className={styles.field}>
            <label htmlFor="subject">Subject</label>
            <input
              id="subject"
              value={subject}
              onChange={(event) => setSubject(event.target.value)}
              autoComplete="off"
              className={styles.wide}
            />
          </div>

          <div className={styles.field}>
            <label htmlFor="fromLocal">From</label>
            <div className={styles.address}>
              <input
                id="fromLocal"
                value={fromLocal}
                onChange={(event) => setFromLocal(event.target.value)}
                autoComplete="off"
                spellCheck={false}
              />
              <span className={styles.at}>@</span>
              <input
                id="fromDomain"
                aria-label="From domain"
                value={fromDomain}
                onChange={(event) => setFromDomain(event.target.value)}
                autoComplete="off"
                spellCheck={false}
              />
            </div>
          </div>

          <div className={styles.field}>
            <label htmlFor="replyTo">Reply-to</label>
            <input
              id="replyTo"
              value={replyTo}
              onChange={(event) => setReplyTo(event.target.value)}
              autoComplete="off"
              spellCheck={false}
              className={styles.wide}
            />
          </div>

          <div className={styles.field}>
            <label htmlFor="markdown">Body</label>
            <textarea
              id="markdown"
              value={markdown}
              onChange={(event) => setMarkdown(event.target.value)}
              spellCheck={true}
              className={styles.body}
            />
            {/* The answer to "can it carry HTML, CSS and JavaScript", where
                somebody would otherwise spend an afternoon finding out. */}
            <p className={styles.medium}>
              Markdown. Email clients strip JavaScript and most CSS, so neither
              is offered here.
            </p>
          </div>

          <div className={styles.field}>
            <span className="meta">Placeholders</span>
            {placeholders.length === 0 ? (
              <p className={styles.medium}>None.</p>
            ) : (
              <ul className={styles.placeholders}>
                {placeholders.map((name) => (
                  <li key={name}>{name}</li>
                ))}
              </ul>
            )}
          </div>
        </fieldset>

        {canManage ? (
          <>
            {asked ? (
              <div className={styles.confirm}>
                {/* Not a warning about this screen. A campaign renders its
                    messages when it is queued, so what has already gone out
                    keeps the wording it had — which means an edit here can
                    leave the template disagreeing with the email somebody
                    received. */}
                <p>
                  Saving writes a new version. A campaign that has already gone
                  out sent the wording this template had then, not this.
                </p>
                <div className={styles.actions} style={{ marginTop: 0 }}>
                  <button
                    type="button"
                    className="button primary"
                    onClick={save}
                    disabled={saving}
                  >
                    {saving ? "Saving…" : "Confirm save"}
                  </button>
                  <button type="button" onClick={() => setAsked(false)}>
                    Cancel
                  </button>
                </div>
              </div>
            ) : (
              <div className={styles.actions}>
                <button
                  type="button"
                  className="button primary"
                  onClick={template ? () => setAsked(true) : save}
                  disabled={saving}
                >
                  {template
                    ? "Save changes"
                    : saving
                      ? "Creating…"
                      : "Create template"}
                </button>
                {template ? (
                  <span className="meta">Version {template.version}</span>
                ) : null}
              </div>
            )}

            {outcome ? (
              <p
                className={
                  outcome.ok ? styles.saved : `${styles.saved} ${styles.failed}`
                }
              >
                {outcome.text}
              </p>
            ) : null}
          </>
        ) : null}
      </div>

      <div className={styles.sticky}>
        <EmailPreview
          fromLocal={fromLocal}
          fromDomain={fromDomain}
          replyTo={replyTo.trim() === "" ? null : replyTo}
          rendered={rendered}
          pending={rendering}
          error={renderError}
        />
      </div>
    </div>
  );
}
