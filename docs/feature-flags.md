# Feature flags

A flag turns one feature off without a code change, and turning it off is not
supposed to be exciting. Nothing else in the system should notice.

## Where they live

One file per service, at the root of that service:

| Service | File | Reads |
|---|---|---|
| atlas | `src/atlas/MorganHacks.Api/features.json` | `enable_hacker_portal_feature` |
| lark | `src/lark/MorganHacks.Lark/features.json` | nothing yet |
| harbor | `src/harbor/MorganHacks.Harbor/features.json` | nothing yet |
| portalweb | `src/portalweb/features.json` | `enable_hacker_portal_feature`, `enable_reading_mode_feature` |
| portaladmin | `src/portaladmin/features.json` | `enable_reading_mode_feature` |
| portalforms | `src/portalforms/features.json` | `enable_reading_mode_feature` |

A service's file lists only the flags that service reads. A flag named in a
service that never asks for it is worse than no flag at all: somebody turns it
off, watches nothing happen, and stops trusting the file.

The names are the same string everywhere -- `enable_thing_feature` -- so one
flag can be found across the repository by searching for it.

## Turning one off

Two ways, and the second beats the first.

**The file**, for a decision you are keeping:

```json
{ "enable_hacker_portal_feature": false }
```

**An environment variable**, for right now. Same name, upper-cased:

```
ENABLE_HACKER_PORTAL_FEATURE=false
```

```bash
# Azure Container Apps
az containerapp update -n ca-atlas-staging -g rg-mh-staging \
  --set-env-vars ENABLE_HACKER_PORTAL_FEATURE=false
```

On Vercel, set it in the project's environment variables. It is read on the
server at request time, so it must not be prefixed `NEXT_PUBLIC_` -- that would
bake the value into the browser bundle at build time and a later change would
appear to do nothing until the next deploy.

The file is registered as the *lowest* priority configuration source, so the
variable wins. Written the other way round -- appending the file instead of
inserting it first -- the variable would be read and then silently overruled by
the file baked into the image beside it, which is the failure the ordering in
`AddFeatures` exists to prevent.

## What off looks like

**404, not 403.** A feature that is off is indistinguishable from a feature
that was never built. 403 says "there is something here and it is not yours",
which is a different and wrong sentence.

**A redirect, not a notice.** `/portal` sends people to the public site. There
is nothing useful to say to somebody holding a link to a portal that is closed,
and the page they want is the one they came from.

**On the server, before anything renders.** A check inside a client component
would ship the page's markup and then navigate away from it -- a flash of
something they were not meant to see.

## Adding one

1. Add the key to the `features.json` of every service that reads it.
2. Add the constant: `Flags` in `libs/features`, or the TypeScript file.
3. Gate it. `.RequireFeature(Flags.Thing)` on an endpoint group; `isOn(THING)`
   in a server component.
4. Test both positions. A test that only checks the off case passes just as
   well when the route is broken.

## A flag on the client

One shape is already here, and it is the awkward one, because `isOn` is
server-only by design — the comment in `lib/features.ts` explains why, and it is
worth reading before trying to call it from a `"use client"` file.

The two halves of the flag therefore resolve differently:

- **Whether the control exists** is a server decision. `isOn(READING_MODE) &&`
  in a server component means the button is not in the page at all.
- **Whether the styling applies** is unconditional CSS. `libs/ui/reading-mode.css`
  ships in every build, and every rule sits inside `[data-reading="on"]`, an
  attribute that is only ever written when the flag is on.

CSS imports are static in Next, so they cannot be made conditional at request
time. This is the honest trade: a few hundred bytes of inert CSS, rather than a
flash of unstyled text or a build-time switch the Vercel environment override
cannot reach. Nothing renders and nothing applies, which is what "off" has to
mean.

`enable_reading_mode_feature` is the one flag shaped this way. Its treatment
lives in `libs/ui/reading-mode.*`, and it is not read on `portalweb`'s `/`:
that route is prerendered, so `cookies()` is never called for it and there is no
first paint to correct.

## Deliberately not built

There is no admin screen for these and no per-user targeting. Flags here are a
switch somebody throws during an incident, not an experiment framework. A
missing `features.json` stops the service rather than defaulting every flag to
off, because a service quietly serving a stripped-down version of itself is
harder to notice than one that will not start.
