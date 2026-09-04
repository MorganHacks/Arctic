import styles from "./events.module.css";

/**
 * A database with no event in it.
 *
 * The most important state on this screen, and the one most likely to be
 * dismissed as furniture. Until this year it was reached by writing SQL by
 * hand, which is why staging has never had an event and somebody's laptop
 * does.
 *
 * So it says what to do rather than what is missing. Somebody meeting it is
 * usually meeting the console for the first time, in an environment where
 * every other screen is also empty and none of them explains why. This one
 * does, and the thing that fixes it is the panel directly above.
 */
export function NoEvents() {
  return (
    <div className="empty">
      <p className={styles.emptyTitle}>No events yet.</p>
      <p className={styles.emptyBody}>
        Forms, applicants and mail all belong to an event, so the rest of the
        console has nowhere to put anything until one exists. Create the first
        one above. A slug and a name are enough to start, and the dates can be
        filled in whenever they are decided.
      </p>
    </div>
  );
}
