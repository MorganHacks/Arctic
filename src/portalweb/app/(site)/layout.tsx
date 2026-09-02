import "./globals.css";

/**
 * The public site, and the reason it sits in a route group.
 *
 * This app now serves two things that share nothing but a hostname: the
 * recruitment page, which is a locked single screen on a deep blue night
 * scene, and the hacker portal, which is a quiet document that scrolls.
 * `globals.css` sets `overflow: hidden` and a dark background on `body`
 * itself, so while it was imported by the root layout it applied to the portal
 * too — a portal that could not scroll past the fold.
 *
 * A route group is the App Router's answer to exactly that: `(site)` is
 * stripped from the URL, so `/` is still `/`, but the stylesheet is now loaded
 * for these routes and not for `/portal`.
 *
 * Nothing else about the public page changed. If you are looking at this
 * because that page broke, the files under this folder are byte for byte what
 * they were in `app/`.
 */
export default function SiteLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return children;
}
