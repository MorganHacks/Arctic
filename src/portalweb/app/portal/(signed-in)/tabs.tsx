"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { signOut } from "../actions";

const tabs = [
  { href: "/portal", label: "Status" },
  { href: "/portal/profile", label: "Profile" },
  { href: "/portal/check-in", label: "Check in" },
  { href: "/portal/messages", label: "Emails" },
] as const;

/**
 * The four screens, and the way out.
 *
 * Check in is shown to everybody rather than only to people who have a code.
 * A tab that appeared the day somebody confirmed would tell them that day what
 * they were supposed to be told by an email, and the screen behind it explains
 * itself for anyone who is not there yet.
 *
 * A client component only because the current tab has to be marked, and
 * `usePathname` is the only way to know which one that is. `aria-current` does
 * the marking; the underline is the visual echo of it, not the other way
 * round, so a screen reader gets the same answer as a sighted reader.
 */
export function Tabs() {
  const pathname = usePathname();

  return (
    <nav className="portal__tabs" aria-label="Your application">
      {tabs.map(({ href, label }) => (
        <Link
          key={href}
          href={href}
          // Exact, because /portal is a prefix of every other tab and a
          // startsWith test would light up "Status" on all three.
          aria-current={pathname === href ? "page" : undefined}
        >
          {label}
        </Link>
      ))}

      {/*
        A form rather than a link, because signing out is a change and a GET
        that changes something is a GET a link prefetcher can make for you.
      */}
      <form action={signOut}>
        <button type="submit" className="link">
          Sign out
        </button>
      </form>
    </nav>
  );
}
