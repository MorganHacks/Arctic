"use client";

import { PlaceholderField } from "./placeholder-field";
import styles from "./templates.module.css";
import type { DraftHandle } from "./use-draft";
import { formatLabel, type Placeholder, type TemplateFormat } from "./types";

/**
 * The form, as three groups that are each about one thing.
 *
 * Split out of the editor so that the component holding the state is not also
 * the component deciding what a label says. They are presentational on
 * purpose: each takes the values it shows and a way to change them, and knows
 * nothing about saving, rendering, or what happens next.
 */

/** Who the message is from, and what it is called. */
export function Identity({
  handle,
  existingKey,
  available,
}: {
  handle: DraftHandle;
  /** The key of a template that already exists, which cannot be changed. */
  existingKey: string | null;
  available: Placeholder[] | null;
}) {
  const { draft, set } = handle;

  return (
    <>
      <div className={styles.field}>
        <label htmlFor="key">Key</label>
        {existingKey ? (
          <p className="mono" style={{ margin: 0 }}>
            {existingKey}
          </p>
        ) : (
          <>
            <input
              id="key"
              value={draft.key}
              onChange={(event) => set("key", event.target.value)}
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
            offers the same names. A menu on one and not the other would read
            as the subject not supporting placeholders at all. */}
        <PlaceholderField
          id="subject"
          value={draft.subject}
          onChange={(value) => set("subject", value)}
          available={available}
          className={styles.wide}
        />
      </div>

      <div className={styles.field}>
        <label htmlFor="fromName">Sender name</label>
        {/* Signed off 2026-09-13. */}
        <p className="meta">
          What the inbox shows instead of the address. Left empty, a mail client
          has only the address to display, so a message from mail@morganhacks.com
          arrives from somebody called &ldquo;mail&rdquo;.
        </p>
        <input
          id="fromName"
          value={draft.fromName}
          onChange={(event) => set("fromName", event.target.value)}
          autoComplete="off"
          maxLength={64}
          className={styles.wide}
        />
      </div>

      <div className={styles.field}>
        <label htmlFor="replyTo">Reply-to</label>
        <input
          id="replyTo"
          value={draft.replyTo}
          onChange={(event) => set("replyTo", event.target.value)}
          autoComplete="off"
          spellCheck={false}
          className={styles.wide}
        />
      </div>
    </>
  );
}

/** What the message says, in whichever of the two languages. */
export function Body({
  handle,
  available,
}: {
  handle: DraftHandle;
  available: Placeholder[] | null;
}) {
  const { draft, set } = handle;

  return (
    <div className={styles.field}>
      <div className={styles.bodyHead}>
        <label htmlFor="body">Body</label>

        {/*
          Switching does not touch what is typed. Converting between the two
          would mean guessing, and a guess that rewrites somebody's body is
          worse than leaving it alone: Markdown in an HTML template renders as
          the plain text it is, which is visible in the preview and undone by
          switching back.
        */}
        <div className={styles.formats} role="group" aria-label="Body language">
          {(["markdown", "html"] as TemplateFormat[]).map((option) => (
            <button
              key={option}
              type="button"
              className={draft.format === option ? "tab on" : "tab"}
              aria-pressed={draft.format === option}
              onClick={() => set("format", option)}
            >
              {formatLabel(option)}
            </button>
          ))}
        </div>
      </div>

      <PlaceholderField
        id="body"
        value={draft.body}
        onChange={(value) => set("body", value)}
        available={available}
        multiline
        spellCheck
        className={styles.body}
      />

      {/* The answer to "what can this carry", where somebody would otherwise
          spend an afternoon finding out -- or find out from an email that has
          already gone. */}
      <p className={styles.medium}>
        {draft.format === "html" ? (
          <>
            {/* Signed off 2026-09-14, and rewritten: a &lt;style&gt; block used
                to be removed, and this said so. It is now kept and its rules
                are also copied onto the elements they match, so the sentence
                that told authors to inline everything by hand was telling them
                to do work the sender now does. */}
            HTML. A &lt;style&gt; block is kept, and its rules are also written
            onto the elements they match, so the design survives a client that
            drops stylesheets. Type set on <code>body</code> is the exception —
            put it on the outermost element instead. Tables are how email lays
            out.
          </>
        ) : (
          <>
            Markdown. Switch to HTML for a design you want control of, a button
            or a coloured panel.
          </>
        )}
      </p>

      {/* Said out loud because a menu nobody knows to summon is the same as no
          menu, which is the state this screen was in. */}
      {available !== null && available.length > 0 ? (
        <p className={styles.medium}>
          Type <span className="mono">{"{{"}</span> to insert a placeholder.
        </p>
      ) : null}
    </div>
  );
}

/**
 * What the body asks for, against what a send can give it.
 *
 * A name the API does not know is the one that will come back refused, and
 * without this the only place that shows up is a campaign that will not go —
 * long after the person who typed it has moved on.
 */
export function Placeholders({ handle }: { handle: DraftHandle }) {
  const { used, resolves } = handle;

  return (
    <div className={styles.field}>
      <span className="meta">Placeholders</span>

      {resolves === null ? (
        <p className={styles.medium}>Placeholder names could not be loaded.</p>
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
                {unknown ? <span className={styles.mark}>Unknown</span> : null}
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
  );
}
