# portalweb

Public site, application form and gated hacker portal. Next.js App Router + TypeScript, deployed to Vercel at `morganhacks.com`.

## Running it

```bash
npm install
npm run dev        # http://localhost:3000
npm run build
npm run typecheck
```

## Current state

One route, `/` — a single hero section. No nav, no sections below, no footer.

## Before this goes live

**Replace the Google Form URL.** `site.config.ts` → `organizerFormUrl` is a placeholder
(`https://forms.gle/REPLACE_ME`). It is the only thing blocking deploy.

## Design tokens

Defined once in `app/globals.css` on `:root`. Do not hardcode these inline, and do not
adjust them for contrast — the palette is already AA-compliant.

| Token | Value | Used for |
|---|---|---|
| `--bg-base` | `#FFFFFF` | page background |
| `--text-primary` | `#0F1A2E` | wordmark, H1, focus ring |
| `--text-secondary` | `#5A6780` | subline |
| `--accent` | `#F58025` | button fill |
| `--accent-hover` | `#E06E14` | button hover |
| `--text-on-accent` | `#1A1206` | button label |
| `--border-subtle` | `#E4E7EC` | reserved, unused so far |

## Layout notes

- Container caps at 1440px with 80px horizontal padding; 140px top, 160px bottom.
- The 72px gap between the wordmark and the hero block is what lands the section at
  ~654px tall, matching the ~650px target. Change it and the section height moves.
- Below 768px: 24px horizontal padding, H1 48/52, subline 18px full-width, button
  full-width. Vertical padding is unchanged on mobile, per spec.
- The button label centers when the button goes full-width on mobile. Page content
  stays left-aligned throughout.

## Conventions

- **Inter is self-hosted** via `next/font/google` — no render-blocking stylesheet.
- **The CTA is an `<a>`, not a `<button>`** so middle-click and "open in new tab" work.
  It carries `rel="noopener noreferrer"`.
- The wordmark is text. Swap it for `next/image` if a logo file lands.
- No OG image yet. Add one at `public/og.png` and uncomment `images` in `app/layout.tsx`.
