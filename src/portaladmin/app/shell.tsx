import Link from "next/link";

/** The frame every signed-in page sits in. */
export function Shell({
  personId,
  children,
}: {
  personId: string;
  children: React.ReactNode;
}) {
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
          <Link href="/people">People</Link>
          <Link href="/audit">Audit</Link>
        </nav>
        {/* The person id rather than their address. Everything else in this
            system logs person_id instead of PII, and a header that quietly
            breaks that rule on every screen is the wrong place to start. */}
        <span className="who" title="Signed in">
          {personId.slice(0, 8)}
        </span>
        <form action="/api/auth/logout" method="post">
          <button type="submit">Sign out</button>
        </form>
      </header>
      <main>{children}</main>
    </div>
  );
}
