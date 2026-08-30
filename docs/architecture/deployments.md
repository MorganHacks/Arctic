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

### Staging is a claimable slot, not a place work accumulates

`morganhacks-cicd.md` defines staging as a shared slot anyone can claim, which
resets to whatever `main` last produced. That survives here — the branch is a
pointer, not somewhere commits pile up:

```bash
# claim staging with your branch
git push origin my-branch:staging --force-with-lease

# reset it to main (do this after a main deploy)
git push origin main:staging --force-with-lease
```

Never merge *into* `staging` and never branch *off* it. The moment someone does,
it becomes the `develop` branch the CI doc warns about.

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
