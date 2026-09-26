"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import Link from "next/link";
import { ArrowRight01Icon, Link04Icon, Tick02Icon } from "@hugeicons/core-free-icons";
import { useCopy } from "@/components/formslist/share-link";
import { EmptyState } from "@/components/ui/empty-state";
import { Icon } from "@/components/ui/icon";
import styles from "./responses.module.css";

export function NoResponses({ formId, code, published, closed }: {
  formId: string;
  code: string;
  published: boolean;
  closed: boolean;
}) {
  const { state, copy } = useCopy(code);
  const canShare = published && !closed;

  return (
    <section className={styles.nothing} aria-label="Form responses">
      <EmptyState variant="data" size="page" title="No responses yet"
        description={canShare
          ? "Share your form to get things started. Responses will appear here as they come in."
          : closed
            ? "This form closed without any responses. Update the deadline to start accepting answers again."
            : "Publish your form and share the link to start collecting responses."}
        action={canShare ? (
          <div className={styles.emptyActions}>
            <button type="button" className={styles.secondaryButton} onClick={copy}>
              <Icon icon={state === "copied" ? Tick02Icon : Link04Icon} size={17} />Copy form link
            </button>
            <span role="status" className={styles.note}>{state === "copied" ? "Link copied" : null}</span>
            <ErrorToast message={state === "failed" ? "Could not copy the link. Try the link above." : null} />
          </div>
        ) : (
          <Link href={`/forms/${formId}`} className={styles.secondaryButton}>
            {closed ? "Manage form" : "Go to editor"}<Icon icon={ArrowRight01Icon} size={16} />
          </Link>
        )} />
    </section>
  );
}
