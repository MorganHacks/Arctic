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
  fromLocal,
  fromDomain,
  replyTo,
  rendered,
  pending,
  error,
}: {
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

      <div className={styles.tray}>
        <div className={styles.envelope}>
          <header>
            <p className={styles.from}>
              {fromLocal || fromDomain ? `${fromLocal}@${fromDomain}` : "—"}
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
    // The ground and the ink, inside the document. Setting them on the
    // iframe element is not enough: the embedded document follows the
    // reader's operating system unless told otherwise, which on a dark
    // machine gave light text on the white ground the frame provides.
    "<style>",
    ":root{color-scheme:light}",
    "html,body{background:#ffffff;color:#14161a}",
    "body{margin:0;padding:1rem 1.1rem;line-height:1.55;",
    "font-family:system-ui,-apple-system,\"Segoe UI\",Roboto,sans-serif}",
    "img{max-width:100%;height:auto}",
    "table{max-width:100%}",
    "</style></head><body>",
    html,
    "</body></html>",
  ].join("");
}
