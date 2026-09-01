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
        <nav>
          <Link href="/people">People</Link>
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
