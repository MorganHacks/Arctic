import type { ReactNode } from "react";
import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { Undo03Icon, ClipboardIcon, FormIcon } from "@hugeicons/core-free-icons";
import { CopyLink } from "@/components/formslist/share-link";
import { Icon } from "@/components/ui/icon";
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
  actions,
  responseCount = 0,
  collapsed = false,
  backAction,
}: {
  form: FormSummary;
  published: { version: number; publishedAt: string | null } | null;
  /** Absent on the responses screen, which is not editing anything. */
  draftVersion?: number;
  tab: "questions" | "responses";
  actions?: ReactNode;
  responseCount?: number;
  collapsed?: boolean;
  backAction?: ReactNode;
}) {
  return (
    <>
      <div className={styles.headerNavigation}>
        {backAction ?? <Link href="/forms" className={styles.back} aria-label="Back to forms">
          <Icon icon={Undo03Icon} size={17} strokeWidth={2} />
          <span>Back</span>
        </Link>}
        {actions}
      </div>

      <div className={styles.headerCollapse} data-collapsed={collapsed} data-responses={tab === "responses"}>
        <div className={styles.headerDetails} inert={collapsed} aria-hidden={collapsed || undefined}>
          <div className={styles.head}>
            <div className={styles.identity}>
              <div className={styles.tags}>
                <span className={styles.kind} data-kind={form.kind}>
                  <Icon icon={form.kind === "application" ? FormIcon : ClipboardIcon} size={16} />
                  {form.kind === "application" ? "Application" : "Survey"}
                </span>

                {/* Which draft is being edited, beside which version is live. They
                    are usually one apart and occasionally several, and somebody who
                    cannot see both has no way to know whether what is on screen is
                    what applicants are answering. */}
                {draftVersion === undefined ? null : (
                  <span className={styles.editing}>Draft {draftVersion}</span>
                )}
              </div>

              <div className={styles.titleRow}>
                <h1>{form.name}</h1>
                <span className={styles.publishStatus} data-live={!!published}>
                  <span />{published ? "Live" : "Draft"}
                </span>
              </div>
              {published ? <p className={styles.liveVersion}>Version {published.version} is available to applicants</p> : null}
            </div>
            <div className={styles.headerLink}><CopyLink code={form.code} name={form.name} appearance="header" /></div>
          </div>

          {/* The other half of the pair. Without it the two halves of a form are
              only reachable through the list, which is a detour on every trip
              between building a form and reading what it collected. */}
          <div className={styles.sectionNavigation}>
            <nav className={styles.tabs} aria-label="Form sections">
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
                  <span className={styles.responseCount}>{responseCount.toLocaleString("en-US")}</span>
                </span>
              ) : (
                <Link href={`/forms/${form.id}/responses`} className={styles.tab}>
                  <Chart />
                  Responses
                  <span className={styles.responseCount}>{responseCount.toLocaleString("en-US")}</span>
                </Link>
              )}
            </nav>
          </div>
        </div>
      </div>
    </>
  );
}
