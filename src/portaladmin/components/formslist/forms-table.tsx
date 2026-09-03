import Link from "next/link";
import type { FormRow } from "@/lib/api";
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
            <td style={{ verticalAlign: "top" }}>
              <Link href={`/forms/${form.id}`}>{form.name}</Link>
              <div className="meta">{form.kind}</div>
            </td>

            <td style={{ verticalAlign: "top" }}>
              <Status form={form} now={now} />
            </td>

            <td style={{ verticalAlign: "top" }}>{form.questions ?? "—"}</td>

            <td style={{ verticalAlign: "top" }}>
              {/* The thing people read aloud at a club meeting and write on a
                  whiteboard, above the thing they paste into a group chat. */}
              <div>
                <ShareCode code={form.code} />
              </div>
              <div style={{ marginTop: "0.35rem" }}>
                <CopyLink code={form.code} />
              </div>
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
