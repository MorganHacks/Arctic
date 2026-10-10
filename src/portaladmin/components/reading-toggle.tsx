"use client";

/**
 * The reading-mode control.
 *
 * COPY: needs sign-off on every string below, per docs/design-brief.md 2.5.
 * The wording is a proposal, not a decision.
 *
 * The component is repeated per app rather than shared from libs/ui, exactly
 * as error-toast.tsx is: React's types do not resolve from that directory, so a
 * shared .tsx there fails typecheck in all three apps. The logic and the styles
 * are shared (libs/ui/reading-mode.ts and reading-mode.module.css) so the three
 * copies cannot drift in behaviour — only this thin render shell repeats.
 */
import { useEffect, useState } from "react";
import { applyReadingMode, prefersReadingMode, rememberReadingMode, storedReadingMode } from "../../../libs/ui/reading-mode";
import styles from "../../../libs/ui/reading-mode.module.css";

export type ReadingToggleProps = {
  /**
   * What the server decided from the cookie.
   *
   * `undefined` means no cookie, so the OS preference decides — and that cannot
   * be decided on the server, hence one step after mount. A stored `off` still
   * wins over a matching OS preference, or turning the mode off would not
   * stick on a machine that asks for more contrast.
   */
  initial?: boolean;
};

export function ReadingToggle({ initial }: ReadingToggleProps) {
  const [on, setOn] = useState(initial ?? false);

  useEffect(() => {
    const stored = storedReadingMode();
    const next = stored === "on" || (stored === undefined && prefersReadingMode());
    setOn(next);
    applyReadingMode(next);
  }, []);

  function toggle() {
    const next = !on;
    setOn(next);
    rememberReadingMode(next ? "on" : "off");
  }

  return (
    <button
      type="button"
      className={styles.toggle}
      aria-pressed={on}
      onClick={toggle}
      // COPY: needs sign-off.
      title="Reading mode"
    >
      {/*
        The accessible name, visually hidden, and identical in both states.

        docs/design-brief.md 2.4: a control that changes meaning must not change
        its accessible name while focused. `aria-pressed` carries the on/off, so
        the announcement is "Reading mode, toggle button, pressed" rather than
        the label swapping under the reader's finger mid-click.
      */}
      <span className={styles.srOnly}>Reading mode</span>
      <span aria-hidden="true" className={styles.mark}>
        Aa
      </span>
    </button>
  );
}