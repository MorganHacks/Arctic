import path from "node:path";
import type { NextConfig } from "next";

/**
 * The API is served from this app's own origin, by rewriting to harbor.
 *
 * The same reasoning as portaladmin, and it matters more here. The session is
 * a cookie with SameSite=Lax, and a browser does not send a Lax cookie on a
 * cross-site fetch — so a portal on one origin calling an API on another
 * simply cannot authenticate. The usual workaround is SameSite=None, which
 * turns the cookie into something every other site can make the browser send,
 * and gives up the CSRF protection Lax was there for.
 *
 * Proxying instead means the browser only ever talks to one origin. Lax keeps
 * working, the cookie stays host-only, and harbor is never exposed to the page
 * directly.
 *
 * It is also what makes the emailed sign-in link work. That link points at
 * `/api/auth/consume` on this origin, so the session cookie is set on the host
 * the applicant is actually browsing rather than on an Azure hostname they
 * have never seen.
 */
/*
 * Harbor, not atlas.
 *
 * Every request here is /api/something, and stripping that prefix is harbor's
 * job -- atlas serves /forms, not /api/forms. Pointed straight at atlas every
 * call 404s, which surfaces as a form that says it does not exist and a
 * console that redirects to sign-in forever, with nothing in any log saying
 * why. The old default was atlas, so it could never have worked.
 */
const apiOrigin = process.env.API_ORIGIN ?? "http://localhost:5050";

const nextConfig: NextConfig = {
  /**
   * The bundler's filesystem root is the repository, not this app.
   *
   * The portal imports its palette from libs/ui/tokens.css, which lives above
   * this directory. Without this the bundler refuses to resolve anything
   * outside src/portalweb, and the two apps end up with a copy of the palette
   * each — which is how a colour comes to mean two different things.
   *
   * Deploying this app therefore needs libs/ present, not just src/portalweb.
   */
  turbopack: {
    root: path.join(import.meta.dirname, "..", ".."),
  },

  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${apiOrigin}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
