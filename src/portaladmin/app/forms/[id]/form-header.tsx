import Link from "next/link";
import { PublicLink } from "@/components/formslist/share-link";
import type { FormSummary } from "@/lib/api";
import styles from "./builder.module.css";
import { Chart, Questions } from "./icons";

/**
 * Who this form is, above both of its halves.
 *
 * One component rather than one per screen. The questions and the responses are
 * two views of the same thing, and a header that drifted between them would
 * make them read as two different forms — which is exactly the confusion the
 * tab pair below it exists to remove.
 *
 * What it says is what somebody arriving needs before anything else: which form
 * this is, whether applicants can answer it right now, and the link. The link
 * is here rather than only on the list because this is the screen somebody is
 * on when they are asked for it.
 */
export function FormHeader({
  form,
  published,
  draftVersion,
  tab,
}: {
  form: FormSummary;
  published: { version: number; publishedAt: string | null } | null;
  /** Absent on the responses screen, which is not editing anything. */
  draftVersion?: number;
  tab: "questions" | "responses";
}) {
  return (
    <>
      <Link href="/forms" className="back">
        ← Forms
      </Link>

      <div className={styles.head}>
        <div className={styles.tags}>
          <span className={styles.kind}>{form.kind}</span>

          {published ? (
            <span className="pill active">Live · v{published.version}</span>
          ) : (
            <span className="pill lapsed">Never published</span>
          )}

          {/* Which draft is being edited, beside which version is live. They
              are usually one apart and occasionally several, and somebody who
              cannot see both has no way to know whether what is on screen is
              what applicants are answering. */}
          {draftVersion === undefined ? null : (
            <span className={styles.editing}>editing v{draftVersion}</span>
          )}
        </div>

        <div className={styles.titleRow}>
          <h1>{form.name}</h1>
        </div>

        <PublicLink code={form.code} />
      </div>

      {/* The other half of the pair. Without it the two halves of a form are
          only reachable through the list, which is a detour on every trip
          between building a form and reading what it collected. */}
      <nav className={styles.tabs}>
        {tab === "questions" ? (
          <span className={styles.tabOn} aria-current="page">
            <Questions />
            Questions
          </span>
        ) : (
          <Link href={`/forms/${form.id}`} className={styles.tab}>
            <Questions />
            Questions
          </Link>
        )}

        {tab === "responses" ? (
          <span className={styles.tabOn} aria-current="page">
            <Chart />
            Responses
          </span>
        ) : (
          <Link href={`/forms/${form.id}/responses`} className={styles.tab}>
            <Chart />
            Responses
          </Link>
        )}
      </nav>
    </>
  );
}
