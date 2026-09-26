import { ArrowRight01Icon, ClipboardListIcon, FormIcon } from "@hugeicons/core-free-icons";
import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { Icon } from "@/components/ui/icon";
import type { FormRow } from "@/lib/api";
import styles from "./forms-page.module.css";
import { CopyLink } from "./share-link";

/**
 * The forms on one event.
 *
 * Three things are on every row, because they are the three questions somebody
 * opens this screen to answer: what is the link, is it live, and how much of
 * the form is written. A form that has never been published is not broken —
 * somebody is still writing it — so that reads as a state rather than as a
 * warning.
 */
export function FormsTable({ forms, now }: { forms: FormRow[]; now: number }) {
  return <>
    <div className={styles.listHeader} aria-hidden="true"><span>Form</span><span>Status</span><span>Questions</span><span>Share link</span><span /></div>
    <ul className={styles.list} aria-label="Forms">
      {forms.map(form => <li className={styles.row} key={form.id}>
        <div className={styles.identity}>
          <span className={styles.formIcon} data-kind={form.kind}><Icon icon={form.kind === "application" ? FormIcon : ClipboardListIcon} size={22} /></span>
          <div><h2><Link href={`/forms/${form.id}`} className={styles.name}>{form.name}</Link></h2>
            <span className={styles.kind}>{form.kind === "application" ? "Application" : "Survey"}</span></div>
        </div>
        <FormStatus form={form} now={now} />
        <div className={styles.questionCount} aria-label={form.questions === null ? "Question count not available" : `${form.questions} ${form.questions === 1 ? "question" : "questions"}`}>
          <strong>{form.questions ?? "—"}</strong><span>{form.questions === 1 ? "question" : "questions"}</span>
        </div>
        <div className={styles.linkCell}><CopyLink code={form.code} name={form.name} /></div>
        <Link href={`/forms/${form.id}`} className={styles.openForm} aria-label={`Open ${form.name}`}><Icon icon={ArrowRight01Icon} size={18} /></Link>
      </li>)}
    </ul>
  </>;
}

export function formStatus(form: FormRow, now: number): "draft" | "closed" | "live" {
  if (!form.published) return "draft";
  return form.closesAt !== null && Date.parse(form.closesAt) <= now ? "closed" : "live";
}

/**
 * Whether this form is the one applicants are filling in right now.
 *
 * Three states rather than two. A published form whose deadline has passed
 * still answers its link, and it shows applicants a page saying so — reading it
 * as "Live" here is how somebody puts a closed form on a flyer.
 */
export function FormStatus({ form, now, showVersion = true }: { form: FormRow; now: number; showVersion?: boolean }) {
  const status = formStatus(form, now);
  return <span className={styles.status} data-state={status}>
    {status === "live" ? <>Live{showVersion && form.publishedVersion !== null ? <span>· v{form.publishedVersion}</span> : null}</> : status === "draft" ? "Draft" : "Closed"}
  </span>;
}
