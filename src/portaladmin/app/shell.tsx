"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

/**
 * The sections, in the order the work happens in.
 *
 * A list rather than five hand-written links so the bar cannot end up marking
 * two sections current, or none.
 */
const SECTIONS = [
  { href: "/events", label: "Events" },
  { href: "/people", label: "People" },
  { href: "/forms", label: "Forms" },
  { href: "/applicants", label: "Applicants" },
  { href: "/mail", label: "Mail" },
  { href: "/templates", label: "Templates" },
  { href: "/audit", label: "Audit" },
];

/**
 * What to call the person reading, and what to put in the square beside it.
 *
 * A name if there is one, otherwise the part of their address before the @,
 * otherwise the start of their id. The id is last because it is the only one
 * of the three that tells a human nothing — an organizer knows their own name
 * and their own address, and has never once memorised their person id.
 */
function signedInAs(personId: string, fullName?: string | null, email?: string | null) {
  const name = fullName?.trim();
  if (name) {
    const parts = name.split(/\s+/);
    const initials = (parts[0][0] + (parts.length > 1 ? parts[parts.length - 1][0] : ""))
      .toUpperCase();
    return { label: name, initials, isId: false };
  }

  const local = email?.split("@")[0]?.trim();
  if (local) {
    return { label: local, initials: local.slice(0, 2).toUpperCase(), isId: false };
  }

  return { label: personId.slice(0, 8), initials: personId.slice(0, 2).toUpperCase(), isId: true };
}

/** The frame every signed-in page sits in. */
export function Shell({
  personId,
  fullName,
  email,
  children,
}: {
  personId: string;
  /**
   * The reader's own name and address, when the caller has them.
   *
   * Optional because `/auth/me` answers with an id and a permission set and
   * nothing else. Until it also returns the name that identity.people already
   * stores, the header falls back down the chain on its own rather than every
   * page having to look one up.
   */
  fullName?: string | null;
  email?: string | null;
  children: React.ReactNode;
}) {
  const pathname = usePathname();
  const who = signedInAs(personId, fullName, email);

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          MorganHacks <span>console</span>
        </div>
        {/* Shown to everybody, including the people whose permissions will
            turn it into a "you do not have audit.view" page. Hiding a link is
            a courtesy where the reader could have it and does not; hiding this
            one would mean an organizer cannot discover the trail exists in
            order to ask for it. */}
        <nav>
          {SECTIONS.map((section) => (
            <Link
              key={section.href}
              href={section.href}
              // Marked rather than merely underlined. Which section you are in
              // is not decoration, and a reader who cannot see the accent
              // should still be told.
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
        {/* Their own name, not their person id.
            Everything in this system logs person_id instead of PII, and that
            rule is about what we record about other people — a trail that
            names an applicant on every row is a trail that has copied the
            applicant database into itself. Showing somebody the name they
            signed in with is not that: it leaves no record, and they already
            know it. The id was carried here from that rule by habit, and to a
            human it is eight characters of noise where the answer to "am I
            still signed in as the right account" should be. It stays only as
            the last fallback, for the case where no name or address reached
            this component at all. */}
        <div className="identity">
          <span className="avatar" aria-hidden="true">
            {who.initials}
          </span>
          <span className={who.isId ? "who id" : "who"} title="Signed in">
            {who.label}
          </span>
        </div>
        <form action="/api/auth/logout" method="post">
          <button type="submit">Sign out</button>
        </form>
      </header>
      <main>{children}</main>
    </div>
  );
}
