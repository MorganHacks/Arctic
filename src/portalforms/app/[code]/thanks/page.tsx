import { PageBackground } from "../page-background";
import type { Metadata } from "next";
import { loadForm } from "@/lib/api";
import { formThemeStyle } from "../../../../../libs/ui/form-theme";
import styles from "./thanks.module.css";

type Props = { params: Promise<{ code: string }> };

export const metadata: Metadata = { title: "Thank you — MorganHacks" };

/**
 * Where somebody lands after submitting.
 *
 * A page rather than a message swapped in on the form, so it survives a
 * refresh and can be shown to somebody who asks "did it go through?". It is
 * reached with `replace`, so Back does not return to a form that can no longer
 * be submitted.
 *
 * Deliberately promises nothing about what happens next. Whether a decision
 * comes by email, and when, is a question for the people running registration
 * — inventing an answer here is how somebody comes to be waiting for a message
 * nobody is sending.
 */
export default async function Thanks({ params }: Props) {
  const { code } = await params;

  // Only for the name. A form that has since closed, or a page opened later
  // from history, still gets a sensible sentence rather than an error — they
  // did submit, and nothing about this page should suggest otherwise.
  const form = await loadForm(code).catch(() => null);

  return (
    <main className={`formTheme ${styles.page}`} style={formThemeStyle(form?.theme)}>
      <PageBackground theme={form?.theme} />
      <div className={styles.content}>
        <h1>{form?.kind === "application" ? "Thank you for applying." : "Thanks for your response."}</h1>
        <p>
          {form
            ? `Your answers to ${form.name} have been recorded.`
            : "Your answers have been recorded."}
          {" "}You’re all set. You can close this page.
        </p>
        <div className={styles.actions}>
          <a href="https://morganhacks.com">Back to MorganHacks</a>
        </div>
      </div>
    </main>
  );
}
