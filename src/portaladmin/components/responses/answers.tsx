import type { FormField } from "@/lib/api";
import styles from "./responses.module.css";

/**
 * Turning stored answers into something a person can scan.
 *
 * Two renderings of the same value, deliberately different. In the table an
 * answer is one line whatever it is, because a row that grows to fit a
 * paragraph makes the fifty rows around it unscannable. In the detail panel it
 * is shown whole, because that is the only place it can be read.
 */

/**
 * Whether the question went unanswered.
 *
 * Empty string and empty array count. An optional question skipped, a text box
 * submitted blank and a checkbox group with nothing ticked are the same fact
 * to somebody reading the row, and showing three different kinds of blank
 * would be three ways of saying nothing.
 */
export function unanswered(value: unknown): boolean {
  return (
    value === null ||
    value === undefined ||
    value === "" ||
    (Array.isArray(value) && value.length === 0)
  );
}

/**
 * The label an option is shown under, or the raw value where there is none.
 *
 * A value with no matching option is not corrupt: options are stored by value
 * and shown by label precisely so that rewording one does not rewrite what
 * past applicants appear to have answered, and deleting one leaves answers
 * behind that still say what was chosen. Showing the stored value is the only
 * truthful thing left to do with it.
 */
function optionLabel(field: FormField | null, value: string): string {
  const option = field?.options.find((candidate) => candidate.value === value);
  return option ? option.label : value;
}

/**
 * One answer as a single line of text.
 *
 * Also what the cell's tooltip carries, so a truncated answer can be read
 * without opening the response.
 */
export function asText(value: unknown, field: FormField | null): string {
  if (unanswered(value)) {
    return "";
  }

  if (typeof value === "boolean") {
    return value ? "Yes" : "No";
  }

  if (Array.isArray(value)) {
    return value
      .map((entry) =>
        typeof entry === "string" ? optionLabel(field, entry) : String(entry),
      )
      .join(", ");
  }

  if (typeof value === "string") {
    return optionLabel(field, value);
  }

  if (typeof value === "number") {
    return String(value);
  }

  // A key the form no longer has, holding a shape no question produces any
  // more. Rendering it as JSON is ugly and is the only thing that does not
  // silently drop something somebody told us.
  return JSON.stringify(value);
}

/** Whether the answer should be set in tabular numerals to compare down a column. */
function numeric(field: FormField | null): boolean {
  return field?.type === "number" || field?.type === "date";
}

/** One answer, in a table cell. */
export function AnswerCell({
  value,
  field,
}: {
  value: unknown;
  field: FormField | null;
}) {
  if (unanswered(value)) {
    return <span className={styles.blank}>—</span>;
  }

  const text = asText(value, field);

  return (
    <span
      className={numeric(field) ? styles.numeric : undefined}
      title={text}
    >
      {text}
    </span>
  );
}

/** One answer, in the panel, at whatever length it actually is. */
export function AnswerBlock({
  value,
  field,
}: {
  value: unknown;
  field: FormField | null;
}) {
  if (unanswered(value)) {
    return <p className={styles.unanswered}>Not answered</p>;
  }

  // Every ticked box on its own line. Joined with commas they run together
  // with the options that contain commas, which the dietary and accessibility
  // questions always do.
  if (Array.isArray(value)) {
    return (
      <ul className={styles.ticked}>
        {value.map((entry, index) => (
          <li key={index}>
            {typeof entry === "string" ? optionLabel(field, entry) : String(entry)}
          </li>
        ))}
      </ul>
    );
  }

  const text = asText(value, field);

  // Paragraph answers keep their line breaks. A five-hundred-word answer to
  // "why do you want to come" reflowed into one block is one nobody reads.
  return (
    <p className={numeric(field) ? styles.numericAnswer : styles.answer}>
      {text}
    </p>
  );
}

/**
 * A file size somebody can judge at a glance.
 *
 * Rounded rather than exact. The question a reviewer has is whether the file
 * is a resume or a scanned photograph of one, and that is answered by "180 KB"
 * against "12.4 MB", never by the byte count.
 */
export function fileSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) {
    return "";
  }

  if (bytes < 1024) {
    return `${bytes} B`;
  }

  const kb = bytes / 1024;
  if (kb < 1024) {
    return `${Math.round(kb)} KB`;
  }

  return `${(kb / 1024).toFixed(1)} MB`;
}

/**
 * When something was submitted, to the minute, in the order that sorts.
 *
 * Year first and no month names, because the whole job on this screen is
 * comparing one row against the fifty around it. Sliced off the ISO string
 * rather than put through a locale formatter: a formatter runs in the server's
 * zone on the server and the reader's in the browser, which is a hydration
 * mismatch on every row and two different answers to "when did this arrive".
 */
export function when(iso: string): string {
  if (typeof iso !== "string" || iso.length < 16) {
    return iso ?? "";
  }

  return `${iso.slice(0, 10)} ${iso.slice(11, 16)}`;
}
