import { currentPerson } from "@/lib/api";
import { Nav } from "./nav";
import { sectionsFor } from "./sections";

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

/**
 * The frame every signed-in page sits in.
 *
 * Rendered on the server so that it can ask what this person may do, which is
 * what decides the sections in the bar. Only the bar itself runs in the
 * browser, and only because marking the current section needs the path.
 *
 * It reads the viewer itself rather than taking the permission set as a prop.
 * Fifteen screens render this component and every one of them has already
 * asked who is signed in, so a prop would be fifteen places to pass the answer
 * from — and the one that forgot, or passed a stale set, would draw a bar that
 * does not match the person in front of it. `currentPerson` is memoised for
 * the length of a request, so asking a second time here is not a second call
 * to /auth/me.
 */
export async function Shell({
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
  const who = signedInAs(personId, fullName, email);

  /*
   * No viewer means no sections, rather than every section.
   *
   * Every screen redirects to /sign-in before it renders this, so null here is
   * the session ending between that check and this one, or /auth/me being
   * unreachable for a moment. Both are states where nothing is known about
   * what this person may do, and a bar drawn optimistically out of that would
   * be offering links whose screens are about to send them to sign in anyway.
   */
  const mine = (await currentPerson())?.permissions ?? new Set<string>();

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          MorganHacks <span>console</span>
        </div>
        {/* Only the sections this person can open. A link whose screen answers
            them with "you do not have people.view" is not a discovery that the
            screen exists, it is a door with nothing behind it, and after the
            second one the reader stops believing the bar.

            Cosmetic, and only cosmetic. The screens each gate themselves
            against atlas and go on doing so — the refusal an address typed by
            hand still lands on is the boundary, this is the courtesy. See
            ./sections.ts for which permission each one is and why. */}
        <Nav sections={sectionsFor(mine)} />
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
