"use client";

import { useEffect, useRef, useState } from "react";

/**
 * Where the public forms site lives.
 *
 * A constant with an override rather than a value read from the API. The code
 * is the only part of the link that comes from anywhere — the host is a fact
 * about the deployment, and asking the API for it would be a round trip to be
 * told something this repository already knows.
 */
const formsOrigin =
  process.env.NEXT_PUBLIC_FORMS_ORIGIN ?? "https://forms.morganhacks.com";

/** What goes on the clipboard: the whole thing, scheme included. */
export function publicLink(code: string): string {
  return `${formsOrigin}/${code}`;
}

/** What goes on the screen. The scheme is noise on every row but the first. */
function shownLink(code: string): string {
  return publicLink(code).replace(/^https?:\/\//, "");
}

/**
 * A form's code, set to be transcribed.
 *
 * Seven characters from an alphabet with no `0`, `O`, `1` or `l`, and lowercase
 * on purpose. Upper-casing it for looks would put `I` back in front of somebody
 * copying it off a whiteboard, which is the exact confusion the alphabet was
 * chosen to avoid.
 *
 * Spaced rather than chunked. Letter-spacing separates the characters for
 * somebody reading them out without putting a hyphen in the middle that then
 * gets typed into the URL bar.
 */
export function ShareCode({ code }: { code: string }) {
  return (
    <span
      className="mono"
      style={{ fontSize: "1.05rem", letterSpacing: "0.2em" }}
    >
      {code}
    </span>
  );
}

/**
 * The link, and one press to have it.
 *
 * This is what actually leaves the console: it goes in a group chat, on a
 * flyer, and into a slide. Retyping it out of a table is how a link gets
 * published with one character wrong, and a wrong character here is a form
 * nobody can reach with no error anywhere to say so.
 *
 * The link is shown as well as copied, because a button that only copies gives
 * no way to check what landed on the clipboard before it is pasted somewhere
 * public.
 */
export function CopyLink({ code }: { code: string }) {
  const [state, setState] = useState<"idle" | "copied" | "failed">("idle");
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(
    () => () => {
      if (timer.current) {
        clearTimeout(timer.current);
      }
    },
    [],
  );

  async function copy() {
    if (timer.current) {
      clearTimeout(timer.current);
    }

    try {
      // Available on a secure origin only, which the console always is. The
      // failure is still handled rather than swallowed: somebody running this
      // over plain http in development would otherwise press the button and
      // watch nothing at all happen.
      await navigator.clipboard.writeText(publicLink(code));
      setState("copied");
    } catch {
      setState("failed");
    }

    timer.current = setTimeout(() => setState("idle"), 4000);
  }

  return (
    <span style={{ display: "inline-flex", alignItems: "center", gap: "0.5rem", flexWrap: "wrap" }}>
      <button
        type="button"
        onClick={copy}
        // The button's own name says what it does; which link it does it to is
        // the row it sits in, and the address is right there to be read.
        aria-describedby={`link-${code}`}
      >
        Copy link
      </button>

      <span id={`link-${code}`} className="mono meta">
        {shownLink(code)}
      </span>

      {/*
       * Announced rather than only shown, and the button's own label is left
       * alone. A control whose name changes under a screen reader's cursor is a
       * different control as far as the reader is concerned, so the outcome is
       * said here instead.
       */}
      <span role="status" className="meta">
        {state === "copied" ? "Copied." : null}
        {state === "failed" ? `Could not copy. The link is ${publicLink(code)}` : null}
      </span>
    </span>
  );
}
