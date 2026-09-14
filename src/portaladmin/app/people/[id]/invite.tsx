"use client";

import { useEffect, useRef, useState } from "react";
import styles from "../people.module.css";

/**
 * The sentence to send somebody, and one press to have it.
 *
 * There is an email now — it goes out when a person joins their first team —
 * and this is still worth having. Most organizers are added in a room, or in a
 * chat where the answer is expected in the same thread, and "go and check your
 * inbox" is a worse reply than the thing itself. It also covers the window
 * before anyone has put them on a team, when nothing has been sent at all.
 *
 * The address is in the message for the reason the email exists: somebody who
 * signs in with the wrong Google account is turned away by a refusal that
 * cannot explain itself, because explaining it would tell strangers which
 * addresses are on the allowlist.
 *
 * The origin comes from the browser rather than a setting. This component is
 * served by the console, so the page's own origin is the console's origin by
 * construction — and a setting would be one more thing that can be stale in an
 * environment nobody deploys often.
 */
export function Invite({ email }: { email: string }) {
  const [origin, setOrigin] = useState("");
  const [state, setState] = useState<"idle" | "copied" | "failed">("idle");
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => setOrigin(window.location.origin), []);

  useEffect(
    () => () => {
      if (timer.current) {
        clearTimeout(timer.current);
      }
    },
    [],
  );

  // COPY: needs sign-off.
  const message =
    `You have been added to the MorganHacks organizer console. ` +
    `Sign in at ${origin} with Google, using ${email} — a different Google ` +
    `account will not be let in.`;

  async function copy() {
    if (timer.current) {
      clearTimeout(timer.current);
    }

    try {
      await navigator.clipboard.writeText(message);
      setState("copied");
    } catch {
      // Handled rather than swallowed: the clipboard is a secure-origin API,
      // and somebody running the console over plain http in development would
      // otherwise press this and watch nothing happen.
      setState("failed");
    }

    timer.current = setTimeout(() => setState("idle"), 4000);
  }

  return (
    <section className="panel">
      <h2>Tell them</h2>
      {/* Shown as well as copied, so what landed on the clipboard can be read
          before it is pasted into a channel. */}
      <p className={styles.invite}>{origin === "" ? "…" : message}</p>
      <div className={styles.inviteActions}>
        <button type="button" onClick={copy} disabled={origin === ""}>
          Copy
        </button>
        {/* The button's own label is left alone — a control that renames
            itself under a screen reader's cursor reads as a different
            control — so the outcome is said here instead. */}
        <span role="status" className="meta">
          {state === "copied" ? "Copied." : null}
          {state === "failed" ? "Could not copy. Select the text above." : null}
        </span>
      </div>
    </section>
  );
}
