# ui

Shared design tokens and components, used by all three Next.js apps —
portaladmin, portalweb, portalforms.

There is no package here. Each app imports this directory by relative path
(`../../libs/ui/tokens.css` and so on) rather than through `node_modules`,
so a change here takes effect in every app without a publish step.

`tokens.css` is the palette. Import it; never write a hex in an app.

`reading-mode.css` and `reading-mode.ts` are OpenDyslexic plus the spacing that
makes it work, behind `enable_reading_mode_feature`. The CSS holds the whole
treatment and every rule sits inside `[data-reading="on"]`; the TypeScript holds
the cookie, the attribute, and the `prefers-contrast` fallback. Both are shared
so the three apps cannot drift.

The toggle itself is a per-app `components/reading-toggle.tsx` rather than a
shared `.tsx` here, exactly as `error-toast.tsx` is: React's types do not
resolve from this directory, so a shared component here fails `tsc` in all
three apps. The logic and the styles are the shared parts.

The announcement reaction picker is inspired by [Rare UI's Emoji reaction](https://www.rareui.com/components/emojireaction), using native emoji, the shared MorganHacks palette, and persisted audience reactions.
