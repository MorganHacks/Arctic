import Image from "next/image";
import logo from "@/public/brands/morganhacks.png";
import styles from "./sign-in.module.css";

/**
 * The only unauthenticated screen.
 *
 * Google, and no password field. Organizer access is tied to an allowlisted
 * account and a Google subject bound on first sign-in, so there is nothing here
 * for a password to be checked against — and the link goes through this app's
 * own origin, so the whole round trip stays on one hostname.
 */
/**
 * Why they were turned away, in the words that lead to the fix.
 *
 * Three different problems with three different answers, and for a long time
 * one sentence for all of them. The commonest by far is the one an admin
 * cannot help with at all: signing in with the wrong Google account, which is
 * easy to do when the browser holds several and picks one.
 *
 * Naming the reason is safe here because it is only ever reached after Google
 * has verified the person controls the address. Somebody who does not control
 * it never sees these sentences, so none of them confirms anything about an
 * address to anybody who did not already own it.
 *
 * COPY: needs sign-off.
 */
function refusal(error: string): string {
  if (error === "revoked") {
    return (
      "That account's access was revoked. Ask an admin to restore it — " +
      "adding the address again will not bring it back."
    );
  }

  if (error === "bound") {
    // Deliberately does not promise an admin can fix it, because today none
    // can: the only write to the binding is the one that creates it, so
    // unlinking needs somebody in the database. Saying "ask an admin" would
    // send the reader to a person with no button to press — which is the
    // failure this whole screen is being rewritten to stop.
    return (
      "That address is already linked to a different Google account. Sign in " +
      "with that one, or tell an admin the link needs changing."
    );
  }

  // Anything else, including a stale link with a code this build does not
  // know. The wrong-account case is named first because it is both the most
  // likely and the only one the reader can fix without anybody's help.
  return (
    "That account is not set up as an organizer. Check you are signed in with " +
    "the Google account an admin added — the browser may have picked " +
    "another one. If it is the right address, ask an admin to add you."
  );
}

export default async function SignIn({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  const { error } = await searchParams;

  return (
    <main className={styles.page} aria-labelledby="signin-title">
      <section className={styles.content}>
        <Image
          src={logo}
          alt="MorganHacks"
          className={styles.logo}
          sizes="192px"
          loading="eager"
        />

        <h1 id="signin-title" className={styles.title}>
          Welcome back
        </h1>
        <p className={styles.description}>
          Sign in to the organizer console.
        </p>

        <div className={styles.actions}>
          {error ? (
            <p className={styles.error} role="alert">{refusal(error)}</p>
          ) : null}

          <a
            className={styles.googleButton}
            href="/api/auth/google"
            aria-describedby="signin-note"
          >
            <Image src="/brands/google.svg" alt="" width={22} height={22} />
            <span>Continue with Google</span>
          </a>
        </div>

        <p id="signin-note" className={styles.note}>
          Use the Google account your admin added.
        </p>
      </section>
      <p className={styles.help}>Need access? Ask your team admin.</p>
    </main>
  );
}
