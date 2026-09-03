import styles from "../people.module.css";

/** One reason a person holds a permission right now. */
export type Source = {
  kind: "team" | "grant";
  /** The team's name. Null for a grant, which has no name of its own. */
  label: string | null;
  expiresAt: string | null;
};

/** One line of the union, and why it is there. */
export type EffectiveRow = {
  permission: string;
  sources: Source[];
  /** When the last of its sources runs out. Null when one of them never does. */
  until: string | null;
  /** Held by something that never expires. Distinct from held by nothing. */
  permanent: boolean;
  sensitive: boolean;
};

/**
 * The union, with the reason for every line of it.
 *
 * The most useful thing on the screen, so it is the first thing on it. A flat
 * list of permission strings answers "what can they do" and stops; the question
 * an admin actually arrives with is "why", and the answer to that is in the
 * second column.
 *
 * It is a table because the three facts are read down their columns rather than
 * across their rows: somebody scanning for what a person can do reads the first,
 * somebody working out what to change reads the second, and somebody checking
 * what lapses before the event reads the third.
 *
 * Nothing here is coloured except an expiry, which is the one fact on the screen
 * that will change on its own while nobody is looking.
 */
export function Effective({ rows }: { rows: EffectiveRow[] }) {
  return (
    <section className="panel">
      <h2>Effective permissions</h2>
      <p className="meta" style={{ marginBottom: "0.75rem" }}>
        The union of every team baseline they still hold and every grant that
        has not expired. This is exactly what the API checks.
      </p>

      {rows.length === 0 ? (
        <p className="meta">
          Nothing. They can sign in and see no screen in this console.
        </p>
      ) : (
        <table className={styles.effective}>
          <thead>
            <tr>
              <th scope="col">Permission</th>
              <th scope="col">From</th>
              <th scope="col" className={styles.until}>
                Until
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.permission}>
                <td className={styles.key}>
                  {row.permission}{" "}
                  {row.sensitive ? (
                    <span className="pill sensitive">sensitive</span>
                  ) : null}
                </td>

                <td>
                  {row.sources.length === 0 ? (
                    /* Only reachable if a team's baseline changed between the
                       two requests that built this page. Shown as a gap rather
                       than filled in with a plausible source, because a
                       plausible source would be believed. */
                    <span className={styles.blank}>—</span>
                  ) : (
                    <ul className={styles.sources}>
                      {row.sources.map((source, n) => (
                        <li className={styles.source} key={n}>
                          <span className={styles.origin}>
                            {source.kind === "team" ? "Baseline" : "Grant"}
                          </span>
                          {source.label ? <span>{source.label}</span> : null}
                        </li>
                      ))}
                    </ul>
                  )}
                </td>

                <td className={styles.until}>
                  {row.sources.length === 0 ? (
                    <span className={styles.blank}>—</span>
                  ) : row.permanent ? (
                    <span className="meta">no expiry</span>
                  ) : (
                    <span className="pill expiring">
                      until {row.until?.slice(0, 10)}
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
