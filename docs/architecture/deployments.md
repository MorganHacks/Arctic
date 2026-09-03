# Deployments and environments

How the frontends are built, where they run, and what to do when you need to
ship something. The Vercel projects, domains and project settings below were
checked against the live account on **2026-09-03**.

Backend services run on Azure Container Apps, not on Vercel:
`deploy/azure/README.md` covers how, and
[rolling back a deploy](../runbooks/rolling-back-a-deploy.md) covers what to do
when one goes wrong. The two halves meet in one place only — the frontends
proxy `/api/*` to harbor from their own origin, and nothing in a browser ever
holds an Azure hostname.

---

## Six services, and where each one runs

| Project | Root directory | Runs on | What it is |
|---|---|---|---|
| `morganhacks-portalweb` | `src/portalweb` | Vercel | public site and the gated hacker portal |
| `morganhacks-portaladmin` | `src/portaladmin` | Vercel | organizer console, RBAC'd by team |
| `morganhacks-portalforms` | `src/portalforms` | Vercel | the public form, one per code |
| `harbor` | `src/harbor` | Azure Container Apps | the gateway, the only thing published |
| `atlas` | `src/atlas` | Azure Container Apps | the API, internal ingress only |
| `lark` | `src/lark` | Azure Container Apps | the mail worker, no ingress |

The gated hacker portal lives **inside** portalweb rather than as its own
deployment. Same person, same session, same brand: a hacker applies and then
logs in to check status. Splitting them would mean two apps sharing auth, a
design system and an API client for one audience.

Two boundaries were worth paying for, and they are different arguments.

**admin**, because it holds PII export and mass-email triggers. Different threat
model, so a different deployment and a smaller blast radius.

**forms**, because it is the one place with no session at all. The code in the
URL is the whole permission, so the app that renders it has no auth, no cookie
and nothing to leak — and it is the app that takes the load when a link goes up
on a lecture-hall slide.

Revisit the portalweb split if the attendee portal grows a live schedule, team
formation, submissions and judging. That is phase two.

---

## Environments

**Custom environments are not available on this plan** — the API reports
`accountLimit.total: 0`, so `vercel deploy --target=staging` will not work.
Staging is therefore a long-lived branch with a domain bound to it.

| Environment | Trigger | portalweb | portaladmin | portalforms |
|---|---|---|---|---|
| Production | push to `main` | `morganhacks.com` | `admin.morganhacks.com` | `forms.morganhacks.com` † |
| Staging | push to `staging` | `main-stg.morganhacks.com` | `admin-stg.morganhacks.com` | `forms-stg.morganhacks.com` † |
| Preview | any other branch or PR | generated `*.vercel.app` | generated `*.vercel.app` | generated `*.vercel.app` |
| Development | local | `localhost:3000` | `localhost:3001` | `localhost:3002` |

† Attached and verified in Vercel, and serving nothing. See below.

Staging subdomains are named after the frontend, not the environment, so the
scheme reads clearly with three of them: `main-stg` is the public site,
`admin-stg` is the console, `forms-stg` is the form.

All seven custom domains are attached and verified in Vercel. `morganhacks.com`
and `www.morganhacks.com` now point at portalweb — the apex 308s to `www`, which
serves the 2027 site. That is a change from the state this document described
before, when both were held by the team, assigned to no project, and serving
404.

**The two `forms` domains are attached to a project that has never built.**
Every portalforms deployment is cancelled by its Ignored Build Step, so there is
no production deployment for the domain to point at and both answer Vercel's
`DEPLOYMENT_NOT_FOUND`. The public form is not deployed. It is the first entry
[on the backlog](../backlog.md).

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
| `staging` | the three `*-stg` domains update |
| `main` | the frontends go to production, and the backend deploys to **staging** |

**Production only ever moves when something lands on `main`.** Vercel's
production branch is `main`, and every other branch produces a preview
deployment, so there is no path from `staging` to production that does not go
through a merge into `main`.

The backend is asymmetric on purpose. `deploy-azure.yml` runs on every push to
`main` that touches `src/atlas`, `src/harbor`, `src/lark`, `libs/` or
`deploy/azure/`, and it deploys **staging** — production is `workflow_dispatch`
only, behind the approval its GitHub environment requires. Staging mirrors main,
which is what makes it a useful rehearsal of what production will become;
promoting is a decision somebody makes rather than a side effect of merging a
pull request.

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

**Only `portalweb`'s force switch is implemented.** Every other service in the
form runs a job that exits 1 saying it has "no staging deploy target yet", which
is no longer true of any of them: portaladmin and portalforms have staging
domains bound to the branch, and atlas, harbor and lark are deployed by
`deploy-azure.yml`. Moving the branch still rebuilds all three frontends, so
what is missing is only the re-run-without-a-code-change path. It is
[on the backlog](../backlog.md).

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

| Setting | Why |
|---|---|
| Root Directory | Without it, builds run from the repo root, where there is no `package.json`, and fail. |
| Ignored Build Step | Skips the build when nothing this project depends on changed, so a portaladmin commit does not rebuild portalweb. |

All three projects set a root directory — `src/portalweb`, `src/portaladmin`,
`src/portalforms` — and all three set `sourceFilesOutsideRootDirectory`, because
each imports its palette from `libs/ui/tokens.css`.

The ignore step is where they differ, and two of the three are wrong:

| Project | Ignored Build Step | Effect |
|---|---|---|
| `morganhacks-portalweb` | `git diff --quiet HEAD^ HEAD ./` | correct for its own directory; a change confined to `libs/ui` skips the build |
| `morganhacks-portaladmin` | none | builds every push |
| `morganhacks-portalforms` | `test ! -d src/portalforms && exit 0 \|\| exit 1` | **skips every build, always** |

The command runs with the project's root directory as the working directory. For
portalforms that is `src/portalforms`, so `test ! -d src/portalforms` is true
from in there, `exit 0` runs, and Vercel cancels the deployment — the log says
so in as many words:

```
Running "test ! -d src/portalforms && exit 0 || exit 1"
The deployment was canceled because the Ignored Build Step command returned exit code 0.
```

Exit 0 means skip, exit 1 means build. It reads like it was written to mean the
opposite. Both this and portalweb's blindness to `libs/` are
[on the backlog](../backlog.md).

Everything else belongs in `vercel.ts` in the project directory.

Set them from the CLI rather than clicking:

```bash
echo '{"rootDirectory":"src/portalweb","commandForIgnoringBuildStep":"git diff --quiet HEAD^ HEAD ./"}' \
  | vercel api "/v9/projects/<project>?teamId=<team>" -X PATCH --input -
```

`vercel api` takes `-F` or `--input`, **not** `-d`. A `-d` body is silently
ignored and the call reports success without changing anything.

---

## What lives in the database, and what does not

Three kinds of rule, deliberately kept apart.

**Invariants go down, as constraints and triggers.** Anything we would be
alarmed to find violated *however it got violated*. The audit trail is the
clearest case: before the trigger, `UPDATE applications SET status='accepted'`
in psql succeeded silently and wrote no history row — which does not leave a
gap in the trail, it leaves a trail that is wrong in a way nobody can detect
afterwards. There is no version of "remember to write the history row" that
survives an incident at 2am.

**Set operations that must be atomic go down, as functions.** SQL is genuinely
better at these than C# is. The RSVP expiry and waitlist promotion job is the
case this exists for: expire everyone past their deadline, count the freed
spots, promote the oldest waitlisted rows to fill them, all consistent for the
duration. In C# that is a round trip per applicant and a window where two runs
double-promote.

**Decisions stay in C#.** Which transitions are legal, and what a person is
permitted to do. These change when the team's thinking changes, and they belong
in the language with the compiler, the tests and the stack traces. `plpgsql`
has none of those, does not appear in Sentry, and is a much steeper ramp for a
contributor than C# is.

The test: would you be alarmed to find it violated regardless of how? Push it
down. Might it change next season? Keep it up.

## Which caller a rate limit counts against

Both front ends call the API from their own server, not from the browser — the
session cookie is `SameSite=Lax` and a browser will not send one cross-site.
That is the right call, and it means the connection harbor sees comes from
Vercel. Partitioning a rate limit on it puts every applicant in the world in one
bucket.

Measured against staging rather than assumed:

| Path | What harbor sees | Where the caller is |
|---|---|---|
| straight to harbor | the caller | — |
| through a front end | Vercel's address | `X-Real-IP`, `X-Vercel-Forwarded-For` |

`X-Forwarded-For` arrives **empty** on a proxied request, which is why the
forwarded-headers middleware never found the caller.

`ClientAddress.ForRateLimit` prefers those headers and falls back to the
connection. It trusts a header, and that is worth being clear about: somebody
reaching harbor directly can put anything in it and get a fresh bucket. It is
still strictly better than one bucket for everybody, and the controls that
actually stop abuse are elsewhere:

- **Per address**, in atlas — three sign-in links per address per quarter hour.
  This is what stops one person being mailed repeatedly, and it does not depend
  on the caller's address at all.
- **Per source volume**, at Cloudflare — the only layer that sees the real
  connection. `harbor/Program.cs` has said this from the beginning: Cloudflare
  absorbs volume, harbor handles the per-identity limits Cloudflare cannot
  express.

**Cloudflare's rate limiting is not configured yet.** Until it is, volume
control from a single source has no home. Worth doing before registration
opens rather than after — it is [on the backlog](../backlog.md) with the rest of
what is outstanding.

## Trusted proxies — required before either service goes live

`harbor` and `atlas` both partition their rate limiters on the caller's IP, and
`atlas` records one on every session row. Behind Cloudflare, and behind an
ingress, that IP is the proxy's rather than the caller's unless we say so.

There are two ways to get this wrong and they fail in opposite directions.

Trust nothing, and every per-IP limit becomes **one bucket shared by the whole
internet**: harbor's `auth-strict` policy is ten requests per fifteen minutes,
so the eleventh person to try signing in anywhere in the world gets a 429. That
is an outage we cause ourselves, not an attack.

Trust anything, and every per-IP limit becomes **no limit at all**, because the
caller chooses which bucket to be counted in.

Both services are meant to read `X-Forwarded-For` only from proxies we name.
That is the whole point of the setting. The usual version of this fix clears the
known-proxy list so the header is always honoured, which is worse than the bug
it fixes: any caller can then set `X-Forwarded-For` to a random address per
request and walk straight past the limiter.

**Neither service does what the paragraph above says, today.** Both call
`KnownProxies.Clear()` and `KnownIPNetworks.Clear()` and then add whatever the
configuration names, and the configuration names nothing in either deployed
environment. ASP.NET's forwarded-headers middleware only performs its
known-proxy check when at least one of those lists is non-empty — so with both
empty there is no check, and the header is honoured from whoever sent it. That
is the failure this section warns about, arrived at from the other direction.

Measured, not read. Against a local atlas with nothing configured, a request
from `127.0.0.1` carrying `X-Forwarded-For: 203.0.113.7` writes `203.0.113.7`
into `identity.sessions.ip`. The full reproduction and what it costs are
[on the backlog](../backlog.md), where this is the first entry.

Filling the settings in fixes it. So would deciding that an empty list should
mean "trust nothing", which is what everything written about this assumes.

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

That check alone is not enough while the lists are empty, because a forged
header produces the same reading as a real one. Sign in a second time with an
`X-Forwarded-For` you made up: if the row records the address you invented, the
header is being trusted from anybody and the limits are decoration.

`Network__ForwardLimit` is already set in `apps.bicep` — 2 for atlas, 1 for
harbor — and it does nothing on its own. It is the hop count, not the trust.

## Cost: the services sleep

The web-facing services run at **zero replicas** by default. A request wakes
one, which takes a few seconds; every request after that is normal. This is
most of the reason an environment costs about $38/month rather than $185 — an
always-on replica is billed for all 730 hours of a month whether or not anybody
visits.

`lark` is the exception and stays at one replica. It polls the mail queue on a
timer, so nothing would ever wake it, and a mail worker scaled to zero is a
queue that silently never sends. It is most of the idle cost of the
environment, and the way to remove it is a KEDA scaler on queue depth rather
than a lower replica count.

### Turning it off for registration

A cold start is fine for an organizer opening the admin console. It is not fine
for an applicant on a deadline. So for the weeks registration is open, and for
the event weekend:

Set `WARM_REPLICAS` to `1` on the GitHub environment and redeploy. Unset it
afterwards. Unset and empty both mean zero.

That is roughly $30/month more while it is set, which is the right thing to
spend it on.

### The logging cap

The Log Analytics workspace is capped at **0.5 GB of ingestion a day**.
Ingestion is charged per gigabyte and is the one line that can run away without
anybody doing anything wrong — a retry loop that logs its own failure will
happily bill for it.

Hitting the cap stops ingestion until the next day. That loses telemetry, not
data. If it is ever hit, fix whatever started shouting rather than raising the
number.

## Environment variables

Never committed. Vercel holds the values. There are three, and only one of them
is set anywhere today.

| Variable | Read by | Where | Without it |
|---|---|---|---|
| `API_ORIGIN` | all three frontends, on the server | **portaladmin only** | falls back to `http://localhost:5050`, so a deployed build proxies every `/api/*` call to itself |
| `NEXT_PUBLIC_FORMS_ORIGIN` | portaladmin, inlined into the bundle | nowhere | share links point at `https://forms.morganhacks.com` — right in production, wrong in staging and locally |
| `FORMS_PREVIEW` | portalforms, on the server | nowhere | the scaffolded preview form stays off, which is what you want |

`API_ORIGIN` missing from portalweb and portalforms is
[on the backlog](../backlog.md); it is currently masked by portalforms not
building at all.

```bash
vercel env add API_ORIGIN production          # main only
vercel env add API_ORIGIN preview staging     # staging branch only
vercel env pull                               # → .env.local, gitignored
```

Branch-scoped preview variables are what make one project serve two
environments without a second project — portaladmin's two entries are exactly
that, one for production and one scoped to the `staging` branch.

There is no `.env.example` in the repository. This table is the contract until
somebody writes one.

---

## Domains

DNS is on **Cloudflare**, not Vercel nameservers. Attaching a domain in Vercel
does nothing until a matching DNS record exists in Cloudflare. Several domains
on this account are attached but dead for exactly that reason.

| Domain | Project | Answers |
|---|---|---|
| `morganhacks.com` | `morganhacks-portalweb` | yes — 308 to `www` |
| `www.morganhacks.com` | `morganhacks-portalweb` | yes |
| `main-stg.morganhacks.com` | `morganhacks-portalweb` (branch `staging`) | yes |
| `admin.morganhacks.com` | `morganhacks-portaladmin` | yes |
| `admin-stg.morganhacks.com` | `morganhacks-portaladmin` (branch `staging`) | yes |
| `forms.morganhacks.com` | `morganhacks-portalforms` | **no — `DEPLOYMENT_NOT_FOUND`** |
| `forms-stg.morganhacks.com` | `morganhacks-portalforms` (branch `staging`) | **no — same** |
| `2023/2024/2025/2026.morganhacks.com` | year archives | yes |
| `quiz.morganhacks.com` | `morgan-hacks-quiz` | **no DNS** |

To bring one up, add in Cloudflare, **DNS only (grey cloud)** so Vercel can
issue its own certificate:

```
A    <subdomain>    76.76.21.21
```

Removing a domain from a project is not the same as removing it from the
account. Detach with `DELETE /v9/projects/<project>/domains/<domain>`; the team
keeps ownership. `vercel domains rm` gives up the domain entirely — do not
reach for it to take a site offline.

`admin.morganhacks.com` used to be held by the older `morgan-hacks-admin`
project and had to be reconciled before `portaladmin` could use it. That is
done: the domain is bound to `morganhacks-portaladmin`, and `morgan-hacks-admin`
is left holding only its `*.vercel.app` name. Vercel's project list still shows
`admin.morganhacks.com` as that project's "latest production URL" — stale
metadata on their side, not a second claim on the domain.

---

## Recipes

```bash
vercel link --repo            # monorepo; writes .vercel/repo.json
vercel ls <project>           # deployment history and status
vercel curl / --deployment <url>   # hit a protected preview
vercel rollback <url>         # revert production
vercel promote <url>          # promote without rebuilding
```

Use `vercel link --repo`. There are three frontends in this repository, and
plain `vercel link` writes `project.json`, which tracks one project and is the
usual way monorepo setups break.

**Do not put a Vercel token in CI for the normal path.** The git integration is
already the pull model — Vercel watches the repo. GitHub Actions owns `ci-gate`
(lint, typecheck, tests, the C# services); Vercel owns building and deploying
the frontends. A token is only needed if you add a job that claims staging.
