import path from "node:path";
import type { NextConfig } from "next";

/**
 * The API is served from this app's own origin, by rewriting to harbor.
 *
 * portaladmin does this because its session is a `SameSite=Lax` cookie that a
 * browser will not send cross-site. There is no session here — these pages are
 * public and the code in the URL is the whole permission — so the reasons are
 * different, but they point the same way.
 *
 * A cross-origin API would need harbor's hostname in the page, which means an
 * `Access-Control-Allow-Origin` entry per environment and a preflight before
 * every submission. Both are things to get wrong on the one night of the year
 * when several hundred people are trying to apply at once, and getting them
 * wrong looks like a form that silently will not submit.
 *
 * Proxying means the browser only ever talks to `forms.morganhacks.com`. No
 * CORS, no preflight, and harbor is never a hostname anybody has to know.
 */
const apiOrigin = process.env.API_ORIGIN ?? "http://localhost:5080";

const nextConfig: NextConfig = {
  /**
   * The bundler's filesystem root is the repository, not this app.
   *
   * All three portals import their palette from libs/ui/tokens.css, which
   * lives above this directory. Without this the bundler refuses to resolve
   * anything outside src/portalforms, and each app ends up with a copy of the
   * palette — which is how a colour comes to mean two different things.
   *
   * Deploying this app therefore needs libs/ present, not just src/portalforms.
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
