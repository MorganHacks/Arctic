"use client";

import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState, useTransition } from "react";
import { addTemplate, editTemplate, previewBody } from "@/app/templates/actions";
import { EmailPreview } from "./email-preview";
import { PlaceholderField } from "./placeholder-field";
import styles from "./templates.module.css";
import {
  placeholdersIn,
  type Placeholder,
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
const FROM_LOCAL = "mail";
const FROM_DOMAIN = "morganhacks.com";

export function Editor({
  template,
  canManage,
  available,
}: {
  template: Template | null;
  canManage: boolean;
  /**
   * The placeholders a send can fill in, or null where the API could not say.
   *
   * Read on the server by the page rather than fetched from here, so the menu
   * is available on the first keystroke instead of after a round trip that
   * would land somewhere in the middle of the first sentence.
   *
   * Null is not an empty list. Empty means the API answered and there is
   * nothing to offer; null means nobody knows, and the difference decides
   * whether a name the author typed can be called unknown — accusing a
   * perfectly good placeholder of being wrong is worse than saying nothing.
   */
  available: Placeholder[] | null;
}) {
  const router = useRouter();

  const [key, setKey] = useState(template?.key ?? "");
  // Broadcast only, here. The API still serves both kinds -- the sign-in link
  // is a transactional template and is sent by atlas, not written here -- but
  // the console has no reason to offer a kind nobody composes by hand.
  const kind: TemplateKind = template?.kind ?? "broadcast";
  const [subject, setSubject] = useState(template?.subject ?? "");
  // One sending identity, not a field. An address somebody types is an address
  // that can be wrong, and a from address that is not verified in SES does not
  // bounce -- it fails to send at all. An existing template keeps whatever it
  // already had, so editing one never silently re-addresses it.
  const fromLocal = template?.fromLocal ?? FROM_LOCAL;
  const fromDomain = template?.fromDomain ?? FROM_DOMAIN;
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
      ? {
          subject: template.subject,
          html: template.html,
          text: template.text,
          // Carried through the seed as well, so a template that was already
          // written with a stylesheet says so on first paint rather than only
          // after the next keystroke triggers a render.
          notes: template.notes,
        }
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
  const used = useMemo(
    () => placeholdersIn(subject, markdown),
    [subject, markdown],
  );

  /**
   * The names that resolve, for looking one up.
   *
   * Null all the way through where the API could not be read, so the list
   * below the editor keeps its old behaviour — every name simply listed — and
   * nothing is marked wrong on the strength of a list nobody has.
   */
  const resolves = useMemo(
    () =>
      available === null
        ? null
        : new Set(available.map((placeholder) => placeholder.name)),
    [available],
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
            <label htmlFor="subject">Subject</label>
            {/* The subject goes through the same renderer as the body, so it
                offers the same names. A menu on one and not the other would
                read as the subject not supporting placeholders at all. */}
            <PlaceholderField
              id="subject"
              value={subject}
              onChange={setSubject}
              available={available}
              className={styles.wide}
            />
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
            <PlaceholderField
              id="markdown"
              value={markdown}
              onChange={setMarkdown}
              available={available}
              multiline
              spellCheck
              className={styles.body}
            />
            {/* The answer to "can it carry HTML, CSS and JavaScript", where
                somebody would otherwise spend an afternoon finding out. */}
            <p className={styles.medium}>
              Markdown. Email clients strip JavaScript and most CSS, so neither
              is offered here.
            </p>
            {/* Said out loud because a menu nobody knows to summon is the same
                as no menu, which is the state this screen was in. */}
            {available !== null && available.length > 0 ? (
              <p className={styles.medium}>
                Type <span className="mono">{"{{"}</span> to insert a
                placeholder.
              </p>
            ) : null}
          </div>

          {/*
            What the body asks for, against what a send can give it.

            The same list as before, now able to disagree with itself. A name
            the API does not know is the one that will come back refused, and
            until now the only place that showed up was a campaign that would
            not go — long after the person who typed it had moved on.
          */}
          <div className={styles.field}>
            <span className="meta">Placeholders</span>
            {resolves === null ? (
              <p className={styles.medium}>
                Placeholder names could not be loaded.
              </p>
            ) : null}
            {used.length === 0 ? (
              <p className={styles.medium}>None.</p>
            ) : (
              <ul className={styles.placeholders}>
                {used.map((name) => {
                  const unknown = resolves !== null && !resolves.has(name);

                  return (
                    <li key={name} className={unknown ? styles.unknown : ""}>
                      {name}
                      {unknown ? (
                        <span className={styles.mark}>Unknown</span>
                      ) : null}
                    </li>
                  );
                })}
              </ul>
            )}
            {resolves !== null && used.some((name) => !resolves.has(name)) ? (
              <p className={styles.medium}>
                A campaign refuses to send a placeholder that does not resolve.
              </p>
            ) : null}
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
