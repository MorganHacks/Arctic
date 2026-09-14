"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { Section } from "./sections";

/**
 * The section bar.
 *
 * Its own component, and the only part of the shell that runs in the browser,
 * because marking the current section needs the path and the path is only
 * readable there. Which sections are in `sections` was decided on the server —
 * shipping the reader's permission set down here to be filtered would put a
 * second copy of the rules in the bundle, and two copies of a rule are two
 * answers to the same question.
 */
export function Nav({ sections }: { sections: Section[] }) {
  const pathname = usePathname();

  /*
   * A sentence where the links would be, rather than an empty bar.
   *
   * Somebody who can open none of them is not hypothetical: a judge holds
   * judging.score_assigned, a volunteer holds checkin.scan, and the console has
   * no screen for either, so both land here with nothing to show. A bar that is
   * simply empty reads as a page that failed to load, and the reader's next
   * move is to reload it twice and then ask why the console is down. This says
   * the account is fine and names the thing that is missing, which is what
   * every refusal screen behind these links already does.
   *
   * The <nav> stays either way. It is the flex child that takes up the slack
   * between the brand and the identity block, so dropping it would slide the
   * whole right-hand side of the header across.
   */
  if (sections.length === 0) {
    return (
      <nav>
        {/* COPY: needs sign-off. */}
        <span className="no-sections">
          Nothing here yet. Ask an admin for access.
        </span>
      </nav>
    );
  }

  return (
    <nav>
      {sections.map((section) => (
        <Link
          key={section.href}
          href={section.href}
          // Marked rather than merely underlined. Which section you are in is
          // not decoration, and a reader who cannot see the accent should still
          // be told.
          aria-current={
            pathname === section.href || pathname.startsWith(`${section.href}/`)
              ? "page"
              : undefined
          }
        >
          {section.label}
        </Link>
      ))}
    </nav>
  );
}
