import { Coverage } from "./coverage";
import styles from "./mail.module.css";
import { Rendered } from "./rendered";
import type { Preview } from "./types";

/**
 * What would arrive, between who it would go to and the button that sends it.
 *
 * The screen's order is its argument. Stage one resolves who; this says what
 * they would read and which of them would read a placeholder instead of their
 * name; stage two is the only place a send exists. Somebody reaching the
 * button has already passed both consequences, in that order, on the way down
 * the page.
 *
 * Full width rather than a third column beside the two panels above. A gap
 * that sits next to the send control is a gap somebody can press past without
 * their eyes crossing it, and the whole value of this panel is that they
 * cannot.
 *
 * Both halves are optional, and the section disappears entirely when the API
 * sends neither. The preview endpoint grew these fields after this screen
 * shipped; against an API that has not got them, the page is exactly the page
 * it was before — a count, a sample and a send — rather than a panel
 * apologising for empty space.
 */
export function Message({ preview }: { preview: Preview }) {
  const coverage = preview.placeholderCoverage ?? [];
  const renders = preview.renders ?? [];

  if (coverage.length === 0 && renders.length === 0) {
    return null;
  }

  return (
    <section className={`${styles.card} ${styles.full} ${styles.revealed}`}>
      <div className={styles.cardHead}>
        <h2>Message</h2>
      </div>

      <div className={styles.cardBody}>
        {/* First, because it is the warning. A rendered message shows one
            person's gap; these numbers say how many people have it. */}
        <Coverage coverage={coverage} />

        {coverage.length > 0 && renders.length > 0 ? (
          <hr className={styles.rule} />
        ) : null}

        {renders.length > 0 ? (
          <Rendered
            renders={renders}
            recipientCount={preview.recipientCount}
          />
        ) : null}
      </div>
    </section>
  );
}
