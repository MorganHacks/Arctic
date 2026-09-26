"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { PlaceholderField } from "./placeholder-field";
import styles from "./templates.module.css";
import settings from "./settings.module.css";
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

/** What the message says, in whichever of the two languages. */
export function Body({
  handle,
  available,
  error,
}: {
  handle: DraftHandle;
  available: Placeholder[] | null;
  error?: string;
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
        spellCheck={draft.format !== "html"}
        invalid={Boolean(error)}
        describedBy={error ? "template-body-error" : undefined}
        className={styles.body}
      />
      {error ? <ErrorToast descriptionId="template-body-error" message={error} /> : null}

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
          Type {"{{"} to insert a placeholder.
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
