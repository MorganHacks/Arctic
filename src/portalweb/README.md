# portalweb

Public site, application form and gated hacker portal. Next.js App Router +
TypeScript, deployed to Vercel at `morganhacks.com`.

## Running it

```bash
npm install
npm run dev        # http://localhost:3000
npm run build
npm run typecheck
```

## Current state

Two things that share a hostname and nothing else:

| Route | What it is |
|---|---|
| `/` | The MorganHacks 2027 organizer application page. **Single screen, no scroll.** |
| `/portal/*` | The hacker portal. A document that scrolls, on the shared palette. |

**All copy on the `/` page is approved by the team. Do not reword it, and do
not add new user-facing text, without asking first.** Facts come from the 2027
recruitment deck, not from the old event site.

**The portal's copy is not signed off yet.** Everything an applicant reads
about their *application* comes from the API — `ApplicantView` in
`MorganHacks.Applications` — so it changes there, not here. The rest (labels,
hints, the sign-in page) is drafted in these files and needs a read-through.

### Why the route group

`app/(site)/` and `app/portal/` each bring their own stylesheet, and the root
layout brings none. `globals.css` sets `overflow: hidden` on `body` to enforce
the single-screen rule; while it was imported by the root layout it applied to
the portal too, which left the portal unable to scroll past the fold. The group
is stripped from the URL, so `/` is still `/`.

Cross-links between the two are plain `<a>`, not `next/link`. A client-side
navigation would carry one group's stylesheet into the other.

## Before this goes live

**Replace the organizer form URL.** `site.config.ts` → `organizerFormUrl` is a
placeholder (`https://forms.gle/REPLACE_ME`). It is the only thing blocking
deploy.

**Reconcile with the live site.** `morganhacks.com` currently serves the
finished MorganHacks 2026 event site (Next.js on Vercel, behind Cloudflare).
This page is a *different* thing and cannot share the apex with it as-is. The
usual pattern is archiving the finished year at `2026.morganhacks.com` and
freeing the apex for the next cycle.

## Configuration

Everything the public page asserts — year, form URL, socials — lives in
`site.config.ts`. Nothing under `app/` hardcodes a year or a URL.

`API_ORIGIN` points at harbor, and defaults to `http://localhost:5080`.

## The portal

Four screens: `/portal` (status), `/portal/profile`, `/portal/messages` and
`/portal/sign-in`. Each page checks the session for itself — the tab row is
chrome, not a gate.

**The API is served from this app's own origin**, by the `/api/*` rewrite in
`next.config.ts`. Not a convenience: the session cookie is `SameSite=Lax` and a
browser will not send a Lax cookie on a cross-site fetch, so a portal on one
origin calling an API on another simply cannot authenticate. It is also what
makes the emailed sign-in link work — the link points at `/api/auth/consume`
here, so the cookie is set on the host the applicant is actually browsing.

Three rules the screens must keep:

1. **The sign-in form says the same thing whether or not the address exists.**
   The API answers identically for both, and that counts for nothing if the
   screen in front of it branches.
2. **No internal status is ever rendered.** The API sends a sentence, not an
   enum, and there is deliberately no mapping on this side to disagree with it.
3. **Read-only is explained, not just disabled.** The reason comes from the API
   and is shown above the form.

## The theme

A **night scene**: deep blue sky, aurora along the horizon, stars, and the M of
MorganHacks drawn as a constellation. HackUTD and Technica were the reference
for *feel* — both build an illustrated place rather than a flat layout — but
nothing here is borrowed from either. An earlier draft used a city skyline and
was scrapped for being too close to HackUTD's.

| Token | Value | Used for |
|---|---|---|
| `--blue-deep` | `#0d1f4d` | sky overhead, footer scrim |
| `--blue` | `#16306e` | horizon |
| `--blue-lift` | `#2a52a8` | the glow at the base |
| `--on-blue` | `#ffffff` | headline, button fill |
| `--on-blue-soft` | `#c8d3f0` | body copy, labels, links |
| `--glow` | `#6f9bff` | aurora, stars, constellation, headline halo |

Tokens are defined once on `:root` in `app/globals.css`. Never hardcode a hex.

### The scene is SVG, not images

`app/scene.tsx` exports `<Aurora />`, `<Stars />` and `<Constellation />`. All
inline SVG, so there is no asset to ship and everything recolours with the
tokens. All three are `aria-hidden` — they are decoration.

- The aurora is four curves closed to the bottom edge, stacked back to front
  with a vertical gradient, plus two lit crests. It uses
  `preserveAspectRatio="xMidYMax slice"`; with `meet`, a short viewport leaves
  gaps at the left and right edges instead of spanning.
- `.screen::after` scrims the base of the aurora so the social links keep
  contrast. It works because it shares the aurora's layer but comes later in
  the DOM.
- The constellation is hidden under 900px, where it would crowd the headline.
- Stars twinkle on a 4.5s loop, disabled under `prefers-reduced-motion`.

## The MLH badge

Hotlinked from MLH's own S3 and pinned to the top-right corner — the same
placement the 2026 site used, and what MLH's badge guidance expects. Do not
vendor a copy into `public/`.

The season tracks the **event**, so this is the **2027** badge, not 2026's. Both
URLs resolve, so a stale season fails silently — check it when the year rolls
over. Season, badge URL and link all live in `site.config.ts`.

It is a plain `<img>`, not `next/image`: the badge is an SVG, so optimisation
buys nothing and `next/image` would need a `remotePatterns` entry for
`s3.amazonaws.com`.

## Type

- **Instrument Serif** (`--font-display`) — the headline, with an italic cut for
  the accent phrase.
- **Inter** (`--font-inter`) — everything else.

## The single-screen rule

`height: 100svh` with `overflow: hidden` on `body`. This is enforced, not just
implied by short content — verified at 1512×805 with `scrollHeight` equal to the
viewport and no horizontal overflow.

**A locked screen cannot scroll to rescue content that does not fit**, so the
page sheds by height instead:

| Viewport height | What happens |
|---|---|
| under 660px | challenge line shrinks; aurora shortens; constellation hides |
| under 540px | challenge line and contact line drop |
| under 480px | **landscape phones** — eyebrow drops, headline/button/countdown all shrink. Nothing else can be shed here, so this block scales instead. Without it the countdown falls off a screen with no scroll to recover it. |
| under 450px | the social links |

Under 380px wide the countdown stacks, because its two columns force the date
to wrap and strand "EST".

**The headline, the countdown and the apply button never drop at any size.**

Verified with no vertical or horizontal overflow, and both the button and
countdown fully in view, at: 320x690, 360x640, 375x667, 390x735, 412x915,
430x800, 540x720, 740x360, 844x390, 820x1050, 1024x768, 1280x720, 1440x900 and
1920x1080.

**The headline, the deadline and the apply button never drop.** If you add content here, add a height
query for it too.

## Layout notes

The copy column is three groups (`.copy__intro`, `.dare`, `.copy__act`) centred
with a viewport-scaled `gap`, so the page breathes on tall screens and
compresses on short ones without any group breaking apart internally.
`space-between` was tried first and pushed the groups to the extremes, which
read as disconnected and crowded the footer links.

The eyebrow carries flanking hairlines that fade outward. Its trailing letter-
space is cancelled with a negative margin so the label optically centres between
them, the same trick the wordmark uses.

- `--edge` computes one shared left margin so the wordmark, eyebrow, headline,
  lede, CTA and footer all sit on the same line. The bars get it from a centred
  `--page-max` container; the stage has to compute it. Change one, change both.
- The copy column is centred: eyebrow, headline, lede, button, then the
  deadline pill and contact line. The deadline sits under the button so it
  reads at the point of action — the top-right corner belongs to the MLH badge.
- Under 760px the headline drops to a 14ch measure so it wraps and keeps its
  weight instead of thinning out, the CTA goes full width, and the top line
  stacks to two centred rows.

## Conventions

- **The CTA is an `<a>`, not a `<button>`** so middle-click and "open in new
  tab" work. It carries `rel="noopener noreferrer"`.
- The wordmark is text. Swap it for `next/image` if a logo file lands.
- No OG image yet. Add one at `public/og.png` and uncomment `images` in
  `app/layout.tsx`.
- Motion is hover-only and disabled under `prefers-reduced-motion`.

## A note on this checkout

The repo sits on an iCloud-synced Desktop, which periodically drops `name 2.ext`
duplicates into `.next`. Those collide with Next's generated types and break
`tsc`. `tsconfig.json` excludes `* 2.ts` for that reason. Moving the repo
somewhere unsynced would remove the problem at the source.
