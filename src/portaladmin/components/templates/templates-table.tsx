import Link from "next/link";
import { when } from "@/components/mail/types";
import styles from "./templates.module.css";
import { kindLabel, type TemplateRow } from "./types";

/**
 * Every template there is.
 *
 * The kind is a column rather than a footnote because it decides where a
 * template can be used: a campaign will not offer a transactional one, and
 * somebody looking for the announcement they wrote last week needs to be able
 * to see which lane it is in without opening it.
 *
 * The subject is shown under the key, not as a column of its own. The key is
 * what a campaign is stored against and what somebody searches for; the
 * subject is how they recognise it.
 */
export function TemplatesTable({ templates }: { templates: TemplateRow[] }) {
  return (
    <table>
      <thead>
        <tr>
          <th>Template</th>
          <th>Kind</th>
          <th>Version</th>
          <th>Updated</th>
        </tr>
      </thead>
      <tbody>
        {templates.map((template) => (
          <tr key={template.key}>
            <td style={{ verticalAlign: "top" }}>
              <Link href={`/templates/${encodeURIComponent(template.key)}`}>
                <span className="mono">{template.key}</span>
              </Link>
              <div className="meta">{template.subject}</div>
            </td>

            <td style={{ verticalAlign: "top" }}>
              <span className={styles.chip}>{kindLabel(template.kind)}</span>
            </td>

            <td style={{ verticalAlign: "top" }} className={styles.numeric}>
              {template.version}
            </td>

            <td style={{ verticalAlign: "top" }} className={styles.numeric}>
              {when(template.updatedAt)}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
