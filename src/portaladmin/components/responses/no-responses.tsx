import Link from "next/link";
import type { FormField } from "@/lib/api";
import styles from "./responses.module.css";

/**
 * The state this screen is in for most of its life.
 *
 * Registration opens once. Every week before that, and every time somebody
 * opens a form they have just built, this is what they get — so it answers the
 * two questions a blank table leaves hanging: is this working, and what is it
 * going to show me.
 *
 * The publish state is the first, because "nothing has been submitted" and
 * "nobody can submit" look identical from here and only one of them is a
 * problem. The questions are the second: they are the columns this table will
 * have, which is both a preview of the screen and the last chance to notice a
 * question is missing while it is still possible to add one.
 */
export function NoResponses({
  formId,
  publishedVersion,
  fields,
}: {
  formId: string;
  publishedVersion: number | null;
  fields: FormField[];
}) {
  return (
    <div className={styles.nothing}>
      <h2>No responses yet</h2>

      <p>
        Nothing has been submitted to this form.{" "}
        {publishedVersion === null
          ? "It has not been published, so nobody can fill it in yet."
          : `It is live as v${publishedVersion}. Answers appear here as they arrive, newest first.`}
      </p>

      <p>
        <Link href={`/forms/${formId}`}>Edit the questions</Link>
      </p>

      {fields.length === 0 ? (
        <p className={styles.note}>This form has no questions yet.</p>
      ) : (
        <>
          <p className={styles.note}>Questions on this form</p>
          <ul className={styles.waiting}>
            {fields.map((field) => (
              <li key={field.key}>
                {field.label.trim() === "" ? (
                  <code>{field.key}</code>
                ) : (
                  field.label
                )}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}
