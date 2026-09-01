# Deployments and environments

How the frontends are built, where they run, and what to do when you need to
ship something. Verified against the live account on 2026-08-30.

Backend services (`atlas`, `harbor`, `lark`) are **not** covered here — they do
not exist yet and will not run on Vercel. See `morganhacks-hosting.md`.

---

## Frontends: two, not three

| Project | Root directory | What it is |
|---|---|---|
| `morganhacks-portalweb` | `src/portalweb` | public site, application form, gated hacker portal |
| *not built yet* | `src/portaladmin` | organizer console, RBAC'd by team |

The gated hacker portal lives **inside** portalweb rather than as its own
deployment. Same person, same session, same brand: a hacker applies on the
public site and then logs in to check status. Splitting them would mean two apps
sharing auth, a design system and an API client for one audience.

The boundary worth paying for is **admin**, because it holds PII export and
mass-email triggers. Different threat model, so different deployment and a
smaller blast radius.

Revisit if the attendee portal grows a live schedule, team formation,
submissions and judging. That is phase two.

---

## Environments

**Custom environments are not available on this plan** — the API reports
`accountLimit.total: 0`, so `vercel deploy --target=staging` will not work.
Staging is therefore a long-lived branch with a domain bound to it.

| Environment | Trigger | portalweb | portaladmin |
|---|---|---|---|
| Production | push to `main` | `morganhacks.com` *(not attached yet)* | `admin.morganhacks.com` |
| Staging | push to `staging` | `main-stg.morganhacks.com` | `admin-stg.morganhacks.com` |
| Preview | any other branch or PR | generated `*.vercel.app` | generated `*.vercel.app` |
| Development | local | `localhost:3000` | `localhost:3001` |

Staging subdomains are named after the frontend, not the environment, so the
scheme still reads clearly once there is more than one app: `main-stg` is the
public site, `admin-stg` is the console.

**Production is deliberately not attached.** `morganhacks.com` and
`www.morganhacks.com` are owned by the team but assigned to no project, so both
serve 404. Attach them only when the team decides to go live:

```bash
vercel domains add morganhacks.com morganhacks-portalweb
vercel domains add www.morganhacks.com morganhacks-portalweb
```

If the plan ever gains custom environments, migrate: it drops the long-lived
branch, which is the one thing this setup does that `morganhacks-cicd.md`
argues against.

### Branch model

Two long-lived branches. Nothing is force-pushed and nothing is claimed.

```
feature branch  →  staging  →  main
                     ↓           ↓
              main-stg.      morganhacks.com
              morganhacks.com   (production)
```

| You push to | What happens |
|---|---|
| a feature branch | preview deployment on a generated URL |
| `staging` | `main-stg.morganhacks.com` updates |
| `main` | production updates, and nothing else does |

**Production only ever moves when something lands on `main`.** Vercel's
production branch is `main`, and every other branch produces a preview
deployment, so there is no path from `staging` to production that does not go
through a merge into `main`.

### staging is a mirror, never a place work lives

Nothing is ever committed to `staging` and no branch is ever merged into it. It
holds a copy of `main`, or a copy of whatever branch someone is testing, and
nothing else.

`.github/workflows/mirror-staging.yml` overwrites `staging` with `main` every
time anything lands on main. It force-pushes rather than merging, because a
merge would imply staging had commits worth keeping and by definition it does
not.

That is also the reset: claiming staging is temporary, and the next thing to
reach main takes it back.

### Choosing what runs on staging

`Actions -> Deploy to staging` gives a form: which branch to put on staging, and
which services to force a rebuild of.

```bash
gh workflow run deploy-staging.yml -f ref=my-branch
gh workflow run deploy-staging.yml -f ref=my-branch -f portalweb=true
```

Moving staging to a different commit is usually all that is needed, because
Vercel rebuilds from the push. The per-service switches exist for the other
case: re-running a service when the code has **not** changed, which a no-op
push cannot trigger.

Forcing a rebuild without a code change needs a Vercel token:

```bash
gh secret set VERCEL_TOKEN -R MorganHacks/Arctic
```

Without it, moving the branch still works — only the force path fails, and it
says so rather than reporting a deploy that did not happen.

The backend services appear in the form but have no staging deploy target yet,
so ticking one fails loudly. They light up as each becomes deployable.

### Putting a branch on staging by hand

```bash
scripts/claim-staging              # the branch you are on
scripts/claim-staging my-branch    # a specific branch
scripts/claim-staging --release    # hand staging back to main
```

Or as a git alias, if you would rather type `git claim`:

```bash
git config --global alias.claim '!f(){ "$(git rev-parse --show-toplevel)"/scripts/claim-staging "$@"; }; f'
```

Do not call it `git stage` — that is already a synonym for `git add`.

The script uses `--force-with-lease` rather than `--force`, so it fails loudly
if somebody claimed staging while you were not looking instead of quietly
throwing their deploy away.

Note that a claim is not permanent: the next merge to `main` will merge main
into whatever is on staging.

```bash
# work
git checkout -b my-change
git push origin my-change            # preview URL, no domain

# put it on staging
git checkout staging && git merge my-change && git push origin staging

# ship it
git checkout main && git merge staging && git push origin main
```

---

## Project settings that are not in code

Two settings live on the Vercel project, because they have to:

| Setting | Value | Why |
|---|---|---|
| Root Directory | `src/portalweb` | Without it, builds run from the repo root, where there is no `package.json`, and fail. |
| Ignored Build Step | `git diff --quiet HEAD^ HEAD ./` | Skips the build when nothing under this project's directory changed, so a portaladmin commit does not rebuild portalweb. |

Everything else belongs in `vercel.ts` in the project directory.

Set them from the CLI rather than clicking:

```bash
echo '{"rootDirectory":"src/portalweb","commandForIgnoringBuildStep":"git diff --quiet HEAD^ HEAD ./"}' \
  | vercel api "/v9/projects/<project>?teamId=<team>" -X PATCH --input -
```

`vercel api` takes `-F` or `--input`, **not** `-d`. A `-d` body is silently
ignored and the call reports success without changing anything.

---

## Trusted proxies — required before either service goes live

`harbor` and `atlas` both partition their rate limiters on the caller's IP, and
`atlas` records one on every session row. Behind Cloudflare, and behind an
ingress, that IP is the proxy's rather than the caller's unless we say so.

Left unconfigured, every per-IP limit becomes **one bucket shared by the whole
internet**: harbor's `auth-strict` policy is ten requests per fifteen minutes,
so the eleventh person to try signing in anywhere in the world gets a 429. That
is an outage we cause ourselves, not an attack.

Both services read `X-Forwarded-For`, but only from proxies we name. This is
deliberate and it is the whole point of the setting. The usual version of this
fix clears the known-proxy list so the header is always honoured, which is
worse than the bug it fixes: any caller can then set `X-Forwarded-For` to a
random address per request and walk straight past the limiter.

**With nothing configured, neither service trusts the header at all.** It
behaves exactly as it did before. That means the setting is inert until someone
fills it in, so it belongs in the deploy checklist rather than in a backlog.

```jsonc
{
  "Network": {
    // Number of proxies actually in front of this service. Each hop you allow
    // is one more X-Forwarded-For entry a caller is permitted to have written.
    "ForwardLimit": 2,

    // Individual addresses. Use for atlas, whose only caller is harbor.
    "KnownProxies": ["10.0.4.17"],

    // CIDR ranges. Use for harbor, in front of which sits Cloudflare.
    "KnownNetworks": ["173.245.48.0/20", "103.21.244.0/22"]
  }
}
```

Set them per environment as `Network__ForwardLimit`,
`Network__KnownProxies__0`, `Network__KnownNetworks__0` and so on.

Cloudflare publishes its ranges at <https://www.cloudflare.com/ips/> and they
change. Pull them at deploy time rather than pasting them here and forgetting.

**How to tell it is right:** sign in, then read `ip` on the new row in
`identity.sessions`. Your own address means it works. A Cloudflare address, or
the ingress's, means the header is not being trusted and every limiter is
sharing one bucket.

## Environment variables

Never committed. Vercel holds the values; the repo documents the contract in
`.env.example`.

```bash
vercel env add API_BASE_URL production          # main only
vercel env add API_BASE_URL preview staging     # staging branch only
vercel env pull                                 # → .env.local, gitignored
```

Branch-scoped preview variables are what make one project serve two
environments without a second project.

---

## Domains

DNS is on **Cloudflare**, not Vercel nameservers. Attaching a domain in Vercel
does nothing until a matching DNS record exists in Cloudflare. Several domains
on this account are attached but dead for exactly that reason.

| Domain | Project | DNS present |
|---|---|---|
| `morganhacks.com` | *unassigned, serves 404* | yes |
| `www.morganhacks.com` | *unassigned, serves 404* | yes |
| `main-stg.morganhacks.com` | `morganhacks-portalweb` (branch `staging`) | **no** |
| `admin-stg.morganhacks.com` | not created yet | **no** |
| `2024/2025/2026.morganhacks.com` | year archives | yes |
| `2023.morganhacks.com` | `morgan-hacks-2023` | **no** |
| `admin.morganhacks.com` | `morgan-hacks-admin` | **no** |
| `quiz.morganhacks.com` | `morgan-hacks-quiz` | **no** |

To bring one up, add in Cloudflare, **DNS only (grey cloud)** so Vercel can
issue its own certificate:

```
A    <subdomain>    76.76.21.21
```

Removing a domain from a project is not the same as removing it from the
account. Detach with `DELETE /v9/projects/<project>/domains/<domain>`; the team
keeps ownership. `vercel domains rm` gives up the domain entirely — do not
reach for it to take a site offline.

`admin.morganhacks.com` is already held by the existing `morgan-hacks-admin`
project. That has to be reconciled before `portaladmin` can use it.

---

## Recipes

```bash
vercel link --repo            # monorepo; writes .vercel/repo.json
vercel ls <project>           # deployment history and status
vercel curl / --deployment <url>   # hit a protected preview
vercel rollback <url>         # revert production
vercel promote <url>          # promote without rebuilding
```

Use `vercel link --repo` once the second frontend exists. Plain `vercel link`
writes `project.json`, which tracks a single project and is the usual way
monorepo setups break.

**Do not put a Vercel token in CI for the normal path.** The git integration is
already the pull model — Vercel watches the repo. GitHub Actions owns `ci-gate`
(lint, typecheck, tests, the C# services); Vercel owns building and deploying
the frontends. A token is only needed if you add a job that claims staging.
