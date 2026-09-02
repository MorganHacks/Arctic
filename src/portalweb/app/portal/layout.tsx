import type { Metadata } from "next";
import { siteConfig } from "@/site.config";
import "./portal.css";

export const metadata: Metadata = {
  title: `MorganHacks ${siteConfig.year} — your application`,
  // Nothing here should ever be indexed or previewed. It is behind a sign-in
  // and everything past it is somebody's personal data. This overrides the
  // public site's metadata for every route under /portal.
  robots: { index: false, follow: false },
};

/**
 * The frame every portal screen sits in.
 *
 * Deliberately thin: a wordmark, and a footer with the one address a person
 * can write to when the portal cannot help them. The tab row belongs to the
 * signed-in group below this, because showing "Profile" and "Messages" to
 * somebody who is not signed in only offers them two more ways to be told to
 * sign in.
 */
export default function PortalLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <div className="portal">
      <header className="portal__bar">
        {/*
          A plain anchor rather than next/link. It leaves the portal for the
          public site, which is the other route group and the other stylesheet
          — a client-side navigation would carry this one along with it.
        */}
        <a className="portal__brand" href="/">
          MorganHacks <span>portal</span>
        </a>
      </header>

      {children}

      <footer className="portal__foot">
        Something wrong, or a question this page does not answer?{" "}
        <a href={`mailto:${siteConfig.contactEmail}`}>
          {siteConfig.contactEmail}
        </a>
      </footer>
    </div>
  );
}
