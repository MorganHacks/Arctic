import { ClipboardListIcon, FormIcon } from "@hugeicons/core-free-icons";
import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { Icon } from "@/components/ui/icon";
import type { FormRow } from "@/lib/api";
import { formThemeStyle } from "../../../../libs/ui/form-theme";
import { FormStatus } from "./forms-table";
import { FormThumbnail } from "./form-thumbnail";
import { FormCardMenu } from "./form-card-menu";
import styles from "./forms-page.module.css";

export function FormsCards({ forms, now, canManage }: { forms: FormRow[]; now: number; canManage: boolean }) {
  return <ul className={styles.cards} aria-label="Forms">
    {forms.map(form => {
      const questions = form.preview?.questions ?? form.questions;
      return <li className={styles.card} key={form.id}>
        <Link href={`/forms/${form.id}`} className={styles.cardPreview} style={formThemeStyle(form.preview?.theme)} aria-label={`Open ${form.name}`}>
          <FormThumbnail form={form} />
        </Link>
        <div className={styles.cardDetails}>
          <div className={styles.cardHeading}>
            <h2><Link href={`/forms/${form.id}`} className={styles.name}>{form.name}</Link></h2>
            <FormCardMenu form={form} canManage={canManage} />
          </div>
          <div className={styles.cardMeta}>
            <span className={styles.cardKind} data-kind={form.kind}>
              <Icon icon={form.kind === "application" ? FormIcon : ClipboardListIcon} size={16} />
              {form.kind === "application" ? "Application" : "Survey"}
              {questions !== null ? <><span className={styles.metaDot}>·</span>{questions} {questions === 1 ? "question" : "questions"}</> : null}
            </span>
            <FormStatus form={form} now={now} showVersion={false} />
          </div>
        </div>
      </li>;
    })}
  </ul>;
}
