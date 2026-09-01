import type { NextConfig } from "next";

/**
 * The API is served from this app's own origin, by rewriting to harbor.
 *
 * Not a convenience. The session is a cookie with SameSite=Lax, and a browser
 * does not send a Lax cookie on a cross-site fetch — so an admin app on one
 * origin calling an API on another simply cannot authenticate. The usual
 * workaround is SameSite=None, which turns the cookie into something every
 * other site can make the browser send, and gives up the CSRF protection Lax
 * was there for.
 *
 * Proxying instead means the browser only ever talks to one origin. Lax keeps
 * working, the cookie stays host-only, and harbor is never exposed to the page
 * directly.
 *
 * It also keeps the OAuth round trip on one origin: Google redirects back here,
 * not to an Azure hostname a person has never seen.
 */
const apiOrigin =
  process.env.API_ORIGIN ?? "http://localhost:5080";

const nextConfig: NextConfig = {
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
