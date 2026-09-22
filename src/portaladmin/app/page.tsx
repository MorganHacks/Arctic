import Link from "next/link";
import { redirect } from "next/navigation";
import { currentPerson } from "@/lib/api";
import { sectionsFor } from "./sections";
import { Shell } from "./shell";
import styles from "./home.module.css";

/**
 * Where an organizer lands, and the answer to the question they arrive with.
 *
 * The screen said "Nothing to do yet" for months, which was true of the
 * product and never true of the person reading it — somebody signing in has
 * either just been added and does not know what they can do, or has been told
 * they can do something and found they cannot. Both are the same question:
 * what does this account actually have.
 *
 * So the home screen is that account. Not a dashboard of counts — those belong
 * to the screens that own them, and a number repeated on two screens is two
 * numbers that disagree by the time somebody notices.
 *
 * ## Why the teams are on it
 *
 * Permissions alone say what somebody may do and not where any of it came
 * from. "You may read applications" and "you may read applications because you
 * are on registration" are different sentences, and only the second one tells
 * a person what to ask for when it is wrong. The permission model is the thing
 * organizers find hardest about this console, and every part of it that can be
 * shown without a permission check is shown here.
 *
 * Individual grants are deliberately absent. The teams explain the ordinary
 * case; a grant is the exception, and the screen that exists to explain
 * exceptions is a person's own page — which sits behind `people.view`, for
 * everybody including its subject.
 */
export default async function Home() {
  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const sections = sectionsFor(person.permissions);
  const areas = grouped(person.permissions);

  return (
    <Shell personId={person.personId}>
      {/* COPY: everything visible on this screen needs sign-off. */}
      <h1 className={styles.who}>{person.email ?? "Your account"}</h1>
      <p className="lede">
        What this account can do, and where each part of it comes from.
      </p>

      {/* Teams and the starting points share a column because neither fills
          one: the permission list is three times the height of either, and
          left beside a single short panel it leaves a hole where a reader
          expects something. */}
      <div className="columns">
        <div className={styles.stack}>
        <section className="panel">
          <h2>Teams</h2>
          {person.teams.length === 0 ? (
            /*
             * Not an error, and worth saying so. An organizer is added to the
             * allowlist first and put on a team second, so this is the state
             * everybody passes through — and without a sentence it reads as
             * something having gone wrong with an account that is working
             * exactly as designed.
             */
            <p className={styles.none}>
              You are not on a team yet. Most of what an organizer can do comes
              from one, so this is usually why a screen turns you away. Ask an
              admin which team you should be on.
            </p>
          ) : (
            <ul className={styles.teams}>
              {person.teams.map((slug) => (
                <li key={slug}>
                  <code>{slug}</code>
                </li>
              ))}
            </ul>
          )}
        </section>

        {sections.length > 0 ? (
          <section className="panel">
            <h2>Where to start</h2>
            {/* The same list the nav is built from, so the two cannot come to
                disagree about what this person can open. Here as well as up
                there because a nav bar is furniture — somebody signing in for
                the first time reads the page, not the chrome. */}
            <ul className={styles.sections}>
              {sections.map((section) => (
                <li key={section.href}>
                  <Link href={section.href}>{section.label}</Link>
                </li>
              ))}
            </ul>
          </section>
        ) : null}
        </div>

        <section className="panel">
          <h2>What you can do</h2>
          {areas.length === 0 ? (
            <p className={styles.none}>
              Nothing yet. Being on the allowlist lets you sign in and nothing
              else, which is the model working rather than a fault.
            </p>
          ) : (
            <dl className={styles.areas}>
              {areas.map(([area, permissions]) => (
                <div key={area} className={styles.area}>
                  {/* Grouped by the half before the dot, because that is how
                      the permissions were named and a flat list of
                      twenty-seven strings is one nobody reads to the end. */}
                  <dt>{area}</dt>
                  <dd>
                    <ul>
                      {permissions.map((permission) => (
                        <li key={permission}>{permission}</li>
                      ))}
                    </ul>
                  </dd>
                </div>
              ))}
            </dl>
          )}
        </section>
      </div>

    </Shell>
  );
}

/**
 * The permissions, gathered under the area each one names.
 *
 * Sorted, and sorted twice: the areas alphabetically so the list does not
 * reorder itself between two people, and the permissions within an area the
 * same way. A screen that shuffles depending on the order a database happened
 * to return rows is one nobody can compare against a colleague's.
 */
function grouped(permissions: Set<string>): [string, string[]][] {
  const areas = new Map<string, string[]>();

  for (const permission of [...permissions].sort()) {
    const dot = permission.indexOf(".");
    const area = dot < 0 ? permission : permission.slice(0, dot);

    areas.set(area, [...(areas.get(area) ?? []), permission]);
  }

  return [...areas.entries()].sort(([a], [b]) => a.localeCompare(b));
}
