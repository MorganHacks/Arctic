import styles from "./mail.module.css";
import type { PlaceholderCoverage } from "./types";

/**
 * Every placeholder the template uses, against the people it would go to.
 *
 * The number that matters is the one nothing else on this screen reports. A
 * placeholder the segment cannot fill breaks nothing: the send succeeds, the
 * count is right, the addresses are right, and twelve people open an email
 * that greets them as `{{firstName}}`. There is no later moment when that
 * becomes visible — the send cannot be recalled — so it is visible here or
 * nowhere.
 *
 * It is `--warn` and never `--stop`. A gap is something somebody goes and
 * fixes in the segment or the template; it is not a failure, and nothing on
 * this screen refuses to proceed because of it.
 *
 * Zero missing is stated rather than left blank. A panel that only appears
 * when something is wrong teaches people that its absence means nothing was
 * checked, which is exactly the doubt this screen exists to remove.
 */
export function Coverage({ coverage }: { coverage: PlaceholderCoverage[] }) {
  // A template with no placeholders in it has nothing to be missing. Silence
  // is right here, where above it would be ambiguous.
  if (coverage.length === 0) {
    return null;
  }

  const gaps = coverage.filter((entry) => entry.missing > 0);

  return (
    <div>
      <h3 className={styles.subhead}>Placeholders</h3>

      <p className={`notice ${gaps.length > 0 ? "warn" : "ok"} ${styles.tight}`}>
        {gaps.length > 0
          ? `${gaps.length} ${term(gaps.length)} missing for some recipients.`
          : "Every placeholder is filled for every recipient."}
      </p>

      <ul className={styles.coverage}>
        {coverage.map((entry) => (
          <Row key={entry.placeholder} entry={entry} />
        ))}
      </ul>
    </div>
  );
}

/**
 * One placeholder, filled and unfilled.
 *
 * Both numbers, always. "388 filled" alone is a fact somebody has to subtract
 * from a count on another panel to make useful, and the twelve are the whole
 * reason to read this.
 */
function Row({ entry }: { entry: PlaceholderCoverage }) {
  const filled = Math.max(0, entry.total - entry.missing);

  // Personal data, shown to make a number findable rather than to hand over a
  // list. Three, whatever the API sent.
  const examples = (entry.examples ?? []).slice(0, 3);

  return (
    <li className={entry.missing > 0 ? styles.gap : undefined}>
      <div className={styles.coverageRow}>
        {/* In braces, because that is how it appears in the template somebody
            would go and edit. */}
        <span className={styles.placeholder}>
          {`{{${entry.placeholder}}}`}
        </span>

        <span className={styles.coverageCounts}>
          <span className={styles.filled}>{filled} filled</span>
          <span className={entry.missing > 0 ? styles.missing : undefined}>
            {entry.missing} missing
          </span>
        </span>
      </div>

      {entry.missing > 0 && examples.length > 0 ? (
        <p className={styles.examples}>For example {examples.join(", ")}</p>
      ) : null}
    </li>
  );
}

/** "placeholder" or "placeholders", so the summary reads as a sentence. */
function term(count: number): string {
  return count === 1 ? "placeholder is" : "placeholders are";
}
