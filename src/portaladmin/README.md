# portaladmin

Organizer console. Sign-in, people, and the review queue as it is built.

Separate deployment from `portalweb` on purpose — this one holds PII export,
blast triggers and role assignment. Different threat model, smaller blast
radius.

## The API is served from this app's own origin

`next.config.ts` rewrites `/api/*` to harbor. That is not a convenience.

The session is a cookie with `SameSite=Lax`, and a browser does not send a Lax
cookie on a cross-site fetch — so an admin app on one origin calling an API on
another simply cannot authenticate. The usual workaround is `SameSite=None`,
which turns the cookie into one that any site can make the browser send and
gives up the CSRF protection Lax was there for.

Proxying means the browser only ever talks to one origin. Lax keeps working,
the cookie stays host-only, and harbor is never exposed to the page directly.
It also keeps the Google round trip on one hostname rather than bouncing a
person through an Azure URL they have never seen.

## Nothing is cached

Every page reads the session and renders somebody's data. A cache that outlives
a request is one that can show an organizer another organizer's view, so
`apiFetch` is `no-store` and no page opts into caching.

## Running it

```bash
npm install
API_ORIGIN=http://localhost:5080 npm run dev
```

`API_ORIGIN` points at harbor. Against staging:

```bash
API_ORIGIN=https://ca-harbor-staging.kindmeadow-f4a89b60.centralus.azurecontainerapps.io npm run build
API_ORIGIN=... npm start
```

**`API_ORIGIN` is read at build time, not at run time.** Next compiles rewrites
into the routes manifest during `next build`, so setting it only when starting
the server does nothing — the build's value is already baked in. Setting it at
run time and wondering why every API call returns 500 with `ECONNREFUSED
127.0.0.1:5080` is the exact shape of that mistake.

On Vercel this is ordinary: it is a build-time environment variable, set per
environment, and changing it needs a redeploy rather than a restart.

## Sign-in and the redirect URI

`/auth/google` answers 503 until `Google:ClientId` and `Google:ClientSecret` are
set on atlas.

The redirect URI registered with Google must be **this app's** origin, not
harbor's:

```
http://localhost:3000/api/auth/google/callback
https://<admin-host>/api/auth/google/callback
```

That catches people out, because the API is the thing doing the OAuth and the
instinct is to register the API's address. The browser is what Google redirects,
and the browser is on the console — which proxies `/api/*` inward. Registering
harbor's address instead produces a callback the console never sees.

It must match character for character, including the scheme and any trailing
path. Google compares the string.

No JavaScript origin is needed. This is a server-side authorization code flow
with PKCE; the browser never talks to Google's SDK.
