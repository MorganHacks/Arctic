import Link from "next/link";
import type { FormRow } from "@/lib/api";
import styles from "./formslist.module.css";
import { CopyLink, ShareCode } from "./share-link";

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
  return (
    <table>
      <thead>
        <tr>
          <th>Form</th>
          <th>Status</th>
          <th>Questions</th>
          <th>Link</th>
        </tr>
      </thead>
      <tbody>
        {forms.map((form) => (
          <tr key={form.id}>
            <td className={styles.cell}>
              <Link href={`/forms/${form.id}`} className={styles.name}>
                {form.name}
              </Link>
              <div>
                <span className={styles.kind}>{form.kind}</span>
              </div>
            </td>

            <td className={styles.cell}>
              <Status form={form} now={now} />
            </td>

            <td className={styles.count}>{form.questions ?? "—"}</td>

            <td className={styles.linkCell}>
              {/* The thing people read aloud at a club meeting and write on a
                  whiteboard, above the thing they paste into a group chat. */}
              <ShareCode code={form.code} />
              <CopyLink code={form.code} />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/**
 * Whether this form is the one applicants are filling in right now.
 *
 * Three states rather than two. A published form whose deadline has passed
 * still answers its link, and it shows applicants a page saying so — reading it
 * as "Live" here is how somebody puts a closed form on a flyer.
 */
function Status({ form, now }: { form: FormRow; now: number }) {
  if (!form.published) {
    return <span className="pill lapsed">Draft only</span>;
  }

  const closed = form.closesAt !== null && Date.parse(form.closesAt) <= now;
  if (closed) {
    return <span className="pill lapsed">Closed</span>;
  }

  return <span className="pill active">Live · v{form.publishedVersion}</span>;
}
