import type { Metadata } from "next";
import { loadForm } from "@/lib/api";

type Props = { params: Promise<{ code: string }> };

export const metadata: Metadata = { title: "Sent — MorganHacks" };

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
    <main className="notice">
      <h1>That is in</h1>
      <p>
        {form
          ? `Your answers to ${form.name} have been recorded.`
          : "Your answers have been recorded."}
      </p>
      <p>
        Nothing else to do. You can close this page.
      </p>
    </main>
  );
}
