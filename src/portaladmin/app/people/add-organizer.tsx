"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { useActionState, useEffect, useRef, useState } from "react";
import { Add01Icon, Copy01Icon, Tick02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { addOrganizer } from "./actions";
import styles from "./members.module.css";

/** Add the allowlisted address first; its detail page handles team access. */
export function AddOrganizer() {
  const [state, action, pending] = useActionState(addOrganizer, {});
  const [email, setEmail] = useState("");
  const [copyState, setCopyState] = useState<"idle" | "copied" | "failed">("idle");
  const resetCopy = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => () => {
    if (resetCopy.current) clearTimeout(resetCopy.current);
  }, []);

  async function copySignInLink() {
    if (resetCopy.current) clearTimeout(resetCopy.current);
    try {
      await navigator.clipboard.writeText(`${window.location.origin}/sign-in`);
      setCopyState("copied");
    } catch {
      setCopyState("failed");
    }
    resetCopy.current = setTimeout(() => setCopyState("idle"), 4000);
  }

  return (
    <section className={styles.section} aria-labelledby="add-people-title">
      <h2 id="add-people-title" className={styles.sectionTitle}>People</h2>
      <div className={styles.divider} />
      <form action={action}>
        <h3 className={styles.subtitle}>Add an organizer</h3>
        <div className={styles.inviteRow}>
          <label htmlFor="organizer-email" className={styles.visuallyHidden}>Email</label>
          <input
            id="organizer-email"
            name="email"
            type="email"
            required
            autoComplete="off"
            placeholder="name@morganhacks.com"
            className={styles.emailInput}
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            aria-describedby="organizer-help"
            disabled={pending}
          />
          <span className={styles.accountType}>Organizer</span>
          <button type="submit" className={styles.inviteButton} disabled={pending || !email.trim()}>
            <Icon icon={Add01Icon} size={16} />
            <span>{pending ? "Adding…" : "Add"}</span>
          </button>
        </div>
        <p id="organizer-help" className={styles.help}>
          Use their Google account email. You’ll choose their teams next.
        </p>
        <ErrorToast message={state.error} revision={state} />
        {state.error && state.personId ? <Link href={`/people/${state.personId}`}>Open their page</Link> : null}
      </form>
      <button type="button" className={styles.copyLink} onClick={copySignInLink}>
        <Icon icon={copyState === "copied" ? Tick02Icon : Copy01Icon} size={15} />
        <span>{copyState === "copied" ? "Link copied" : "Copy sign-in link"}</span>
      </button>
      <p className={styles.visuallyHidden} role="status">
        {copyState === "copied" ? "Sign-in link copied to clipboard." : ""}
      </p>
      <ErrorToast message={copyState === "failed" ? "Could not copy the link. Try again." : null} />
    </section>
  );
}
