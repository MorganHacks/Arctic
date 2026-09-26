"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useEffect, useRef, useState } from "react";
import { Copy01Icon, Link04Icon, Tick02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import styles from "./formslist.module.css";
import pageStyles from "./forms-page.module.css";

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

/** The address without its last segment, which is the code, shown separately. */
function shownHost(code: string): string {
  return shownLink(code).slice(0, -code.length);
}

/**
 * A form's code, set to be transcribed.
 *
 * Seven characters from an alphabet with no `0`, `O`, `1` or `l`, and lowercase
 * on purpose. Upper-casing it for looks would put `I` back in front of somebody
 * copying it off a whiteboard, which is the exact confusion the alphabet was
 * chosen to avoid.
 *
 * Letter-spaced rather than chunked, and never wrapped. Spacing separates the
 * characters for somebody reading them out without putting a hyphen in the
 * middle that then gets typed into the URL bar — and a code that broke across
 * two lines at a narrow width would grow one in the reader's head anyway.
 */
export function ShareCode({ code }: { code: string }) {
  return <span className={styles.code}>{code}</span>;
}

/**
 * Copying, and saying whether it worked.
 *
 * Shared by the two places the link is offered so they cannot come to disagree
 * about what lands on the clipboard — the list copies a link somebody is about
 * to paste into a group chat, and the builder copies the same link while it is
 * still being written.
 */
export function useCopy(code: string) {
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

  return { state, copy };
}

/**
 * What the outcome of a press is, said rather than only shown.
 *
 * The button's own label is left alone. A control whose name changes under a
 * screen reader's cursor is a different control as far as the reader is
 * concerned, so the outcome goes here instead.
 */
function CopyState({
  state,
  code,
}: {
  state: "idle" | "copied" | "failed";
  code: string;
}) {
  return (
    <>
      <span role="status" className={styles.copyState}>{state === "copied" ? "Copied." : null}</span>
      <ErrorToast message={state === "failed" ? `Could not copy. The link is ${publicLink(code)}` : null} />
    </>
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
export function CopyLink({ code, name, appearance = "compact" }: { code: string; name?: string; appearance?: "compact" | "header" }) {
  const { state, copy } = useCopy(code);
  const address = (
    <span id={`link-${code}`} className={pageStyles.address} title={publicLink(code)}>
      <span>{shownHost(code)}</span><strong>{code}</strong>
    </span>
  );

  return (
    <span className={pageStyles.copyWrap} data-appearance={appearance} data-copied={state === "copied"}>
      {appearance === "header" ? (
        <button type="button" onClick={copy} className={pageStyles.headerCopy}
          aria-label={name ? `Copy link to ${name}` : "Copy link"}
          aria-describedby={`link-${code}`} title={state === "copied" ? "Link copied" : publicLink(code)}>
          <Icon icon={state === "copied" ? Tick02Icon : Link04Icon} size={16} />
          {address}
        </button>
      ) : (
        <span className={pageStyles.copyRow}>
          <Icon icon={Link04Icon} size={15} />
          {address}
          <button
            type="button"
            onClick={copy}
            className={pageStyles.copyButton}
            // The button's own name says what it does; which link it does it to is
            // the row it sits in, and the address is right there to be read.
            aria-describedby={`link-${code}`}
            aria-label={name ? `Copy link to ${name}` : "Copy link"}
            title={state === "copied" ? "Copied" : "Copy link"}
          >
            <Icon icon={state === "copied" ? Tick02Icon : Copy01Icon} size={16} />
          </button>
        </span>
      )}
      <span role="status" className={pageStyles.copyFeedback}>{state === "copied" ? "Link copied." : null}</span>
      <ErrorToast message={state === "failed" ? `Could not copy. The link is ${publicLink(code)}` : null} />
    </span>
  );
}

/**
 * The same link, drawn as the object it is.
 *
 * On the builder there is one form rather than a table of them, so the address
 * is not a cell in a column — it is the single string on the screen that will
 * end up outside the console, and it is drawn as something to pick up rather
 * than as prose to read past. The host is set back and the code carries the
 * weight, because the host is the same on every form and the code is the part
 * anybody has to get right.
 */
export function PublicLink({ code }: { code: string }) {
  const { state, copy } = useCopy(code);

  return (
    <span className={styles.chip}>
      <span id={`chip-${code}`} className={styles.chipLink}>
        <span className={styles.chipHost}>{shownHost(code)}</span>
        <ShareCode code={code} />
      </span>

      <button
        type="button"
        onClick={copy}
        className={styles.chipCopy}
        aria-label="Copy link"
        aria-describedby={`chip-${code}`}
      >
        <Icon icon={Copy01Icon} size={14} />
      </button>

      <CopyState state={state} code={code} />
    </span>
  );
}
