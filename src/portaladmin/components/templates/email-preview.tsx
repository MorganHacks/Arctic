"use client";

import { useState } from "react";
import styles from "./templates.module.css";
import type { Rendered } from "./types";

/**
 * The email, as the sender would build it.
 *
 * Nothing here renders markdown. The html and the text arrive already rendered
 * from the API — the same code path a real send goes through — because the
 * only preview worth having is the one that cannot disagree with what goes
 * out.
 *
 * The body sits in a sandboxed iframe. Three reasons, all of them the same
 * reason: email HTML must not inherit the console's stylesheet or the preview
 * flatters it, the console's stylesheet must not be reachable from it either,
 * and a fully sandboxed frame runs no script — which is exactly what every
 * email client does with one.
 */
export function EmailPreview({
  fromName,
  fromLocal,
  fromDomain,
  replyTo,
  rendered,
  pending,
  error,
}: {
  fromName: string | null;
  fromLocal: string;
  fromDomain: string;
  replyTo: string | null;
  rendered: Rendered | null;
  pending: boolean;
  error: string | null;
}) {
  const [part, setPart] = useState<"html" | "text">("html");

  return (
    <div>
      <div className={styles.head} style={{ marginBottom: "0.6rem" }}>
        <h2 style={{ margin: 0 }}>Preview</h2>

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

      {rendered?.notes?.length ? (
        <ul className={styles.notes}>
          {/*
            Above the preview rather than below it. The preview looks
            deliberate -- that is the whole problem with it -- so the
            explanation has to sit where it is read before somebody decides
            the layout is fine.
          */}
          {rendered.notes.map((note) => (
            <li key={note}>{note}</li>
          ))}
        </ul>
      ) : null}

      <div className={styles.tray}>
        <div className={styles.envelope}>
          <header>
            <p className={styles.from}>
              {/* Name first, address after, the way an inbox shows it. Seeing
                  only the address here is how a sender name nobody set goes
                  unnoticed until the mail has gone out. */}
              {fromName ? `${fromName} ` : null}
              {fromLocal || fromDomain
                ? `${fromName ? "<" : ""}${fromLocal}@${fromDomain}${fromName ? ">" : ""}`
                : "—"}
              {replyTo ? ` · Reply-to ${replyTo}` : null}
            </p>
            <p className={styles.subject}>{rendered?.subject || "—"}</p>
          </header>

          {rendered === null ? (
            <p className={styles.plain}>Nothing to preview yet.</p>
          ) : part === "html" ? (
            <iframe
              className={styles.frame}
              title="Preview"
              sandbox=""
              srcDoc={page(rendered.html)}
            />
          ) : (
            <pre className={styles.plain}>{rendered.text}</pre>
          )}
        </div>
      </div>

      {error ? (
        <p className="error" style={{ marginTop: "0.6rem", marginBottom: 0 }}>
          {error}
        </p>
      ) : (
        <p className={styles.saved}>{pending ? "Rendering…" : null}</p>
      )}
    </div>
  );
}

/**
 * The rendered body, wrapped in just enough document to be one.
 *
 * The wrapper sets a margin, a line height and one font stack, and no colour
 * at all: what an inbox does to an email's typography is the inbox's business,
 * and a frame that painted it would be making a promise no client keeps.
 */
function page(html: string): string {
  return [
    "<!doctype html><html><head><meta charset=\"utf-8\">",
    /*
     * Two declarations, and deliberately not a third.
     *
     * This used to set the typeface, the line height, a page padding and a
     * max-width on images and tables. All four were improvements to how the
     * preview looked and all four were lies: the body shown here is the body
     * that gets sent, and a template that inherited its font from this frame
     * arrived in a real inbox with a different one. The bug that started this
     * was somebody asking why valid HTML "looks weird in that sandbox" — it
     * was not the HTML, it was the four rules underneath it.
     *
     * What is left is one declaration, and even that one is chosen carefully.
     * An embedded document follows the reader's operating system unless told
     * otherwise, and on a dark machine that gave light text on the white
     * ground the frame provides — which no mail client would have done. Naming
     * the colour scheme fixes that by telling the browser which defaults to
     * use, rather than by painting over them.
     *
     * It replaced an explicit `html,body{background:#fff}`, which fixed the
     * same problem and broke a subtler one: a background set on `html` stops
     * the body's own background propagating to the canvas, so an email whose
     * body is a colour filled its own box and left the rest of the frame
     * white. It looked like a short background in the preview and was correct
     * in every real client.
     *
     * Everything else is the email's own business. If it looks unstyled here,
     * it will arrive unstyled, and that is the preview working.
     */
    "<style>:root{color-scheme:light}</style></head><body>",
    html,
    "</body></html>",
  ].join("");
}
