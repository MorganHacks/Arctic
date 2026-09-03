import styles from "./applicants.module.css";
import { StatusPill, markClass, stamp } from "./status";
import type { Step } from "./types";

/**
 * How this application got where it is.
 *
 * Written by a database trigger on every status change, including the ones no
 * screen made — the applicant submitting, the RSVP expiry job, somebody fixing
 * a row by hand during the event. That is why it is worth showing: it is the
 * only account of the application's life that is complete.
 *
 * Oldest first. It is read as a sequence rather than scanned for the newest
 * thing, and a sequence that runs backwards has to be read backwards to make
 * sense.
 *
 * The actor is a person id and never an address, because that is all the table
 * holds. Resolving one to a person needs `people.view`, which most of the
 * registration team does not have — so the id is shown as an id rather than
 * quietly dropped, and somebody who needs the name has somewhere to start.
 */
export function History({ steps }: { steps: Step[] }) {
  if (steps.length === 0) {
    return <p className="meta">Nothing recorded yet.</p>;
  }

  return (
    // Drawn as a rail with a mark against each step, because this is read as a
    // sequence and a list of separate rows is read as a set of unrelated
    // facts. The mark takes the colour of the status the step arrived at, so
    // the trail carries the same four families as the pills beside it.
    <ul className={styles.trail}>
      {steps.map((step, index) => (
        <li key={`${step.at}-${index}`} className={markClass(step.to)}>
          <div className={styles.step}>
            {step.from ? (
              <>
                <StatusPill status={step.from} />
                <span className="meta">→</span>
              </>
            ) : null}
            <StatusPill status={step.to} />
            <span className={styles.note}>{stamp(step.at)}</span>
          </div>

          <div className={styles.actor}>
            {/* Null is a real answer and the honest one: the applicant did it
                themselves, the expiry job did it, or somebody fixed the row by
                hand. Putting a name against a decision nobody made would be
                worse than admitting there is not one. */}
            {step.actorId ? step.actorId.slice(0, 8) : "no organizer"}
            {step.batchId ? " · part of a batch" : null}
          </div>

          {step.reason ? <p className={styles.reason}>{step.reason}</p> : null}
        </li>
      ))}
    </ul>
  );
}
