"use client";

import { useState } from "react";
import styles from "./mail.module.css";
import type { Render } from "./types";

/**
 * The message, as the people on the list would get it.
 *
 * A count and a list of addresses answer "who", and nothing on this screen
 * used to answer "what". That gap is the expensive one: a send cannot be
 * recalled, and the thing most worth catching before it goes — a greeting with
 * the braces still in it, a subject line built from a field nobody filled in —
 * is only visible in a rendered message.
 *
 * The html and the text arrive already rendered from the API, by the same code
 * path a send goes through. Nothing here renders anything: a preview this
 * screen built itself could disagree with what goes out, and a preview that
 * can disagree is worse than none.
 *
 * Both parts are offered because both are sent. An inbox that refuses HTML
 * gets the text, and a preview that only showed the HTML would be checking
 * half of what leaves.
 *
 * The body sits in a fully sandboxed iframe, the same way the template editor
 * shows one. Email HTML must not inherit the console's stylesheet, or the
 * preview flatters it in a way no inbox will; the console's stylesheet must
 * not be reachable from it either; and a sandboxed frame runs no script, which
 * is what every email client does with one anyway.
 */
export function Rendered({
  renders,
  recipientCount,
}: {
  renders: Render[];

  /** Everybody, so the sample can say what it is a sample of. */
  recipientCount: number;
}) {
  const [at, setAt] = useState(0);
  const [part, setPart] = useState<"html" | "text">("html");

  // Clamped rather than reset: previewing again hands back a new sample, and
  // it may be shorter than the one somebody was stepping through.
  const index = Math.min(at, renders.length - 1);
  const render = renders[index];

  if (!render) {
    return null;
  }

  const unfilled = render.unfilled ?? [];

  return (
    <div>
      <div className={styles.subheadRow}>
        <h3 className={styles.subhead}>Rendered message</h3>

        <div className="tabs">
          <button
            type="button"
            className={part === "html" ? "tab on" : "tab"}
            onClick={() => setPart("html")}
          >
            HTML
          </button>
          <button
            type="button"
            className={part === "text" ? "tab on" : "tab"}
            onClick={() => setPart("text")}
          >
            Text
          </button>
        </div>
      </div>

      <div className={styles.tray}>
        <div className={styles.envelope}>
          <header>
            <p className={styles.to}>To {render.email}</p>
            <p className={styles.subject}>{render.subject}</p>

            {/*
              The per-person half of the coverage numbers, said on the message
              it happened to. A reader who has just seen "12 missing" and then
              watches one of the twelve render is being shown the same fact
              twice on purpose — once as a number, once as a consequence.
            */}
            {unfilled.length > 0 ? (
              <p className={styles.unfilled}>
                Unfilled here: {unfilled.map((name) => `{{${name}}}`).join(", ")}
              </p>
            ) : null}
          </header>

          {part === "html" ? (
            <iframe
              // Keyed so stepping to another recipient loads a new document
              // rather than mutating the one on screen.
              key={`${index}-html`}
              className={styles.frame}
              title="Rendered message"
              sandbox=""
              srcDoc={wrap(render.html)}
            />
          ) : (
            <pre className={styles.plain}>{render.text}</pre>
          )}
        </div>
      </div>

      <div className={styles.stepper}>
        <button
          type="button"
          onClick={() => setAt(index - 1)}
          disabled={index === 0}
        >
          Previous
        </button>
        <button
          type="button"
          onClick={() => setAt(index + 1)}
          disabled={index >= renders.length - 1}
        >
          Next
        </button>

        {/* What is on screen, and what it is a sample of. Said here rather
            than left implied, because five rendered messages beside a count of
            four hundred is otherwise easy to read as all of them. */}
        <p className={`count ${styles.stepCount}`}>
          {index + 1} of {renders.length} sampled from {recipientCount}
        </p>
      </div>
    </div>
  );
}

/**
 * The rendered body, wrapped in just enough document to be one.
 *
 * A margin, a line height and one font stack, and no colour at all: what an
 * inbox does to an email's typography is the inbox's business, and a frame
 * that painted it would be making a promise no client keeps.
 *
 * The template editor writes the same wrapper. Two copies rather than one
 * shared helper because the two screens are owned separately and a change to
 * how one previews email must not silently change the other.
 */
function wrap(html: string): string {
  return [
    '<!doctype html><html><head><meta charset="utf-8">',
    // The ground and the ink, inside the document.
    //
    // Setting them on the iframe element is not enough: the embedded
    // document has its own colour scheme, which follows the reader's
    // operating system unless it is told otherwise. On a machine set to
    // dark that made the default text light, on the white ground the frame
    // provides -- washed out and barely readable. A mail client shows an
    // HTML email on white with dark text whatever the reader prefers, so
    // this says so explicitly rather than inheriting anything.
    "<style>",
    ":root{color-scheme:light}",
    "html,body{background:#ffffff;color:#14161a}",
    "body{margin:0;padding:1rem 1.1rem;line-height:1.55;",
    'font-family:system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}',
    "img{max-width:100%;height:auto}",
    "table{max-width:100%}",
    "</style></head><body>",
    html,
    "</body></html>",
  ].join("");
}
