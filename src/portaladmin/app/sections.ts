/**
 * The console's sections, and what somebody has to hold for each one to show
 * them anything.
 *
 * One table rather than a condition written beside each link. The bar is the
 * only place where every screen is named at once, and the moment "who is this
 * for" is spread across seven `&&`s in JSX it stops being reviewable: nobody
 * can see that two sections disagree about the same permission, and a section
 * added next month gets whatever its author happened to remember.
 *
 * ## Cosmetic, like every other permission check on this side
 *
 * Hiding a link hides nothing. Each screen fetches what it shows from atlas,
 * atlas refuses that read without the permission, and the refusal is the
 * boundary. It does not move because of anything in this file and must not be
 * taken out on the grounds that the link is gone — somebody who types /audit
 * into the address bar still gets the audit screen's "you do not have
 * audit.view" page, and that page is the proof the gate is still where it was.
 *
 * What this does fix is the bar offering a comms organizer a People tab and an
 * Audit tab that have never been able to do anything but refuse them. A tab
 * that only ever refuses teaches its reader that the console is broken, which
 * is the opposite of what a refusal screen is for.
 *
 * ## What each section needs
 *
 * The permission on a row is the one atlas puts on the read that screen is
 * built out of, because without that read there is no screen — only the
 * refusal:
 *
 * - Events, Forms and Applicants all read lists scoped to an event, and
 *   `GET /admin/events`, `GET /admin/forms` and `GET /admin/applicants` are
 *   each behind `applications.view`.
 * - People is `GET /admin/people`, behind `people.view`.
 * - Mail is `GET /admin/campaigns`, behind `email.view_stats`.
 * - Audit is `GET /admin/audit`, behind `audit.view`.
 *
 * Deliberately not listed: the permissions that let somebody change what is on
 * those screens — `forms.manage`, `events.manage`, `announcements.post`,
 * `email.send_broadcast`, `people.manage_teams`, `people.grant_permissions`.
 * Holding one of those without the matching read still lands on the refusal,
 * so putting them here would put back exactly the tab this file exists to
 * remove. The panels and buttons inside each screen are gated on them
 * separately, where they mean something.
 *
 * `needs` is a list, read as any-of, even though every row holds one string
 * today. The unions are coming: a screen whose content has two independent
 * sources — sponsors, or check-in, where viewing stats and scanning are
 * separate permissions on the same page — is useful to the holder of either,
 * and a shape that only fits one permission would be rewritten on the day that
 * lands.
 *
 * ## Why the strings are spelled out here
 *
 * These are atlas's, from `Permission.All` in MorganHacks.Identity, and this
 * is a copy of seven of them. The catalogue deliberately is not copied into
 * this repo — `Catalogue` in lib/api.ts says why — but the failure modes are
 * not the same. A catalogue that drifts is a permission an admin can never
 * grant; a typo here hides a section from everybody, which is loud, reported
 * within the hour, and cannot expose anything, because the server gate never
 * consulted this file.
 */

/** A place in the nav bar. */
export type Section = { href: string; label: string; badge?: number; children?: Section[] };

/** A section, with the permissions any one of which makes it worth opening. */
type GatedSection = Section & { needs: readonly string[] };

/**
 * The sections, in the order the work happens in.
 *
 * A list rather than seven hand-written links so the bar cannot end up marking
 * two sections current, or none.
 */
const SECTIONS: readonly GatedSection[] = [
  { href: "/events", label: "Events", needs: ["applications.view"] },
  { href: "/people", label: "People", needs: ["people.view"] },
  { href: "/forms", label: "Forms", needs: ["applications.view"] },
  { href: "/applicants", label: "Applicants", needs: ["applications.view"] },
  { href: "/mail", label: "Mail", needs: ["email.view_stats"] },
  { href: "/templates", label: "Templates", needs: ["email.manage_templates", "email.delete_templates"] },
  { href: "/audit", label: "Audit", needs: ["audit.view"] },
];

/**
 * The sections this person can open, in order.
 *
 * Returns the plain `Section` and not the row it came from, so the permissions
 * stay on this side: the bar in the browser is told where it may go, not what
 * the rule was, and cannot start making the decision a second time.
 */
export function sectionsFor(mine: ReadonlySet<string>, badges: Readonly<Record<string, number>> = {}): Section[] {
  return [
    { href: "/", label: "Home" },
    ...SECTIONS.filter((section) =>
      section.needs.some((permission) => mine.has(permission)),
    ).map(({ href, label }) => ({
      href,
      label,
      badge: badges[href],
      ...(href === "/forms" && mine.has("forms.manage")
        ? { children: [{ href: "/forms#new-form", label: "New Form" }] }
        : {}),
    })),
  ];
}
