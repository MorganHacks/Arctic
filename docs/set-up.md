# Getting set up

Target: a clone to a running stack in **under 30 minutes**. If it takes you
longer, that is a bug in this document — open an issue.

---

## What you need first

| | Version | Check with |
|---|---|---|
| .NET SDK | **10.x** | `dotnet --list-sdks` |
| Node | **24.x** | `node -v` |
| Docker | any recent | `docker info` |

`curl` and `openssl` too, which macOS and every Linux already have.

.NET 10 rather than 8: .NET 8 leaves support in November 2026, roughly when
this platform would be running.

---

## 1. Start the whole thing

```bash
git clone https://github.com/MorganHacks/Arctic.git
cd Arctic
deploy/local/dev.sh you@morgan.edu
```

That is the whole setup. The script checks its tools, brings up the containers,
waits for Postgres, applies migrations, seeds the address you gave it as a super
admin, starts the four services, and opens the organizer console with you
already signed in. Ctrl+C stops everything it started.

| | Where | |
|---|---|---|
| Postgres | `localhost:5432` | one database, one schema per module |
| Azurite | `localhost:10000` | Azure Blob emulator; resumes land here |
| Mailpit | `localhost:8025` | see [watching a sign-in](#watching-a-hacker-sign-in) before you wait on this |
| atlas | `localhost:5080` | the API |
| harbor | `localhost:5050` | the gateway — **not optional**, see below |
| portaladmin | `localhost:3001` | the organizer console |
| portalforms | `localhost:3002` | the public form, at `/<code>` |

Logs go to `.local-logs/`. The containers are deliberately left running on exit,
because they hold the database and tearing that down every evening means seeding
a super admin every morning. `docker compose down` when you actually want them
gone.

The address is optional — `deploy/local/dev.sh` on its own starts everything and
signs nobody in. Passing one is what makes the console usable, so pass one.

`ARCTIC_TARGET=staging deploy/local/dev.sh` points the local consoles at
staging's harbor instead, for driving a real environment's data through a local
UI. It still starts a local atlas and harbor and still needs their ports free,
despite printing a line that says it does not — read that message as "the
consoles are not using them".

Production is refused and does not become an option: this script seeds super
admins and mints sessions, and neither is a thing to do to the environment
applicants are using.

### harbor is not optional, and this is the part that costs an evening

Both consoles proxy `/api/*` to their own origin — `next.config.ts` in each
rewrites to `API_ORIGIN`, which defaults to harbor on `:5050`. atlas serves
`/auth/me` and `/forms/<code>`, **not** `/api/auth/me` and `/api/forms/<code>`.
Stripping that prefix is harbor's job.

Point a console straight at atlas and every call 404s inside the proxy. The
public form says it does not exist; the console redirects to sign-in forever.
Neither says why, because from the browser's point of view nothing failed.

Provable in three commands, with atlas and harbor both running:

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5080/auth/me      # 401
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5080/api/auth/me  # 404
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5050/api/auth/me  # 401
```

401 is the right answer for "no session". The 404 in the middle is the one that
never surfaces anywhere a person can see it.

### The development sign-in door

Organizer sign-in is Google and only Google. That is right for a deployed
environment and it left local development needing an OAuth client to look at a
page, which is a poor trade for a team that changes every year. So there is a
door:

```
GET /dev/sign-in?email=you@morgan.edu&next=/forms
```

`dev.sh` opens it for you through the console, at
`http://localhost:3001/api/dev/sign-in?email=…`.

It is not a bypass. It issues a real session through the same `SessionService`
the Google callback uses and sets the same cookie, so every request after it is
authenticated exactly as any other request is. There is no branch anywhere that
treats a request as signed in without a session row behind it — writing that
branch is the thing this exists to avoid.

**It cannot exist in a deployed environment, for two independent reasons.**

- atlas registers the route only when `app.Environment.IsDevelopment()`. Every
  deployed container sets `Staging` or `Production` explicitly, in Bicep.
- harbor's route allowlist has no catch-all, and `/api/dev/{**catch-all}` is
  declared in `appsettings.Development.json` — a file only loaded when the
  environment is Development.

Either one alone would be enough, and neither depends on the other. Checked
rather than assumed:

```bash
cd src/atlas/MorganHacks.Api
ASPNETCORE_ENVIRONMENT=Staging \
  dotnet bin/Debug/net10.0/MorganHacks.Api.dll --urls http://localhost:5081

curl -s -o /dev/null -w '%{http_code}\n' \
  'http://localhost:5081/dev/sign-in?email=you@morgan.edu'   # 404, empty body
```

The binary directly rather than `dotnet run`, because `launchSettings.json` sets
`ASPNETCORE_ENVIRONMENT=Development` and overrides the one you exported — which
makes the check pass for the wrong reason.

The person still has to exist in `identity.people` and not be revoked. The door
finds who you say you are; it does not create them.

If you want a session without the browser — for a `curl` loop, or on a machine
where the console will not open — `deploy/local/sign-in.sh you@morgan.edu`
writes the session row directly and prints the cookie to paste. It talks to
Postgres and not to atlas, which is why it needs the database credentials and
why it does not exist anywhere those are not on your machine.

---

## When you need the pieces separately

`dev.sh` is a sequence, not magic. When it fails, or when you want one service
under a debugger, here is the same thing by hand. Read `.local-logs/` first —
whichever step failed has its own file there.

### Containers

```bash
docker compose up -d
docker compose ps          # all three up, postgres healthy
```

Credentials are `arctic` / `local-dev-only` for Postgres, and Azurite's own
published development account for storage — atlas already has both in
`appsettings.Development.json`, so there is nothing to set. They are meant to be
boring and public: nothing here should ever hold real data.

### Schema

```bash
cd src/atlas
dotnet run --project MorganHacks.Migrations                # apply
dotnet run --project MorganHacks.Migrations -- --whatif    # list, change nothing
```

Safe to run repeatedly: applied scripts are journalled in a `schemaversions`
table and skipped on the next run.

`MorganHacks.Migrations` is the **only** thing that changes the database
structure. Do not add DDL anywhere else, including
`deploy/local/postgres/01-schemas.sql` — that file creates the four schemas and
nothing more. Two things migrating the same database is the documented way
setups like this break.

The connection string comes from `ARCTIC_DB`, defaulting to the local compose
stack.

#### Seeding a super admin

Nobody can grant permissions to anyone until at least one person holds
`people.grant_permissions`, and the only way to get the first one is to seed it:

```bash
ARCTIC_SUPER_ADMIN_EMAIL=you@morganhacks.com \
  dotnet run --project MorganHacks.Migrations
```

Idempotent, and it never removes anyone: taking access away is a deliberate act,
not a side effect of a deploy. It warns while there is only one super admin,
because the RBAC doc asks for two so that one graduation cannot lock the org
out.

### The services

Four terminals, in this order. atlas first, because harbor health-checks it.

```bash
# atlas
cd src/atlas && dotnet run --project MorganHacks.Api --urls http://localhost:5080

# harbor
cd src/harbor && dotnet run --project MorganHacks.Harbor --urls http://localhost:5050

# the consoles
cd src/portaladmin && PORT=3001 API_ORIGIN=http://localhost:5050 \
  NEXT_PUBLIC_FORMS_ORIGIN=http://localhost:3002 npm run dev
cd src/portalforms && PORT=3002 API_ORIGIN=http://localhost:5050 npm run dev
```

`npm install` first in each console, once per clone.

`NEXT_PUBLIC_FORMS_ORIGIN` matters more than it looks. The console shows and
copies the public address of a form, and its default is
`forms.morganhacks.com` — a link nobody can open yet, and not the form running
two ports away.

Then:

```bash
curl http://localhost:5080/health        # {"status":"ok"}
curl http://localhost:5050/api/health    # {"status":"ok"}
```

`/health` is liveness only and deliberately does not touch the database — a
Postgres blip that restarts every pod turns a recoverable problem into an
outage. harbor's is its own endpoint rather than a proxied one, for the same
reason, so a 200 there says harbor is up and says nothing about atlas. To check
the path through, ask for something atlas owns:

```bash
curl -s http://localhost:5050/api/forms/nosuchcode
# {"error":"No form with that code."}   ← atlas answered
```

An empty 404 body means harbor matched no route and never called anything.

### The public site

`dev.sh` does not start `portalweb`. It is the marketing site and the hacker
portal, and neither is needed to work on forms or on the console:

```bash
cd src/portalweb
npm install
npm run dev          # http://localhost:3000
```

It reaches the API the same way the others do — its own origin, rewritten to
harbor.

Emailed sign-in links are built from `PublicBaseUrl` on atlas, which defaults to
`http://localhost:3000` and therefore needs nothing set locally. In a deployed
environment it is the portal's real origin, set from `PUBLIC_BASE_URL`.

---

## Google sign-in for organizers

Optional locally, because the development door above exists. Without credentials
`/auth/google` answers 503 and everything else still works, so nobody needs a
Google project to develop the rest.

```bash
export Google__ClientId=...apps.googleusercontent.com
export Google__ClientSecret=...
export Google__RedirectUri=http://localhost:5080/auth/google/callback
```

Google authenticates; it does not authorise. An address must also exist as an
`organizer` row, which is the allowlist. The Google subject id is bound on the
first successful sign-in, so changing a Google email later does not lock anyone
out, and nobody can claim an allowlisted address they do not control.

---

## Turn the git hooks on

One line, once per clone:

```bash
git config core.hooksPath .githooks
```

| Hook | Stops |
|---|---|
| `pre-commit` | `.env` files, iCloud `name 2.ext` duplicates, and obvious secrets (private keys, AWS ids, GitHub and Slack tokens, `NEXT_PUBLIC_*SECRET=`) |
| `pre-push` | pushing straight to `main` or `staging` |

`--no-verify` walks past either, so this is not security. It is here for the
mistakes that actually happen: a `.env` staged by a wildcard `git add`, and
finishing on `main` out of habit.

Two of these are not hypothetical. An iCloud duplicate already broke `tsc` by
colliding with Next's generated types, and the stack doc singles out a prior
hackathon platform that shipped storage credentials in `NEXT_PUBLIC_*`
variables, where anyone could read them out of the client bundle.

---

## Watching a hacker sign in

Organizers use the door above. Hackers use a magic link, and locally that link
never reaches an inbox.

**Nothing on your machine sends mail.** mailpit is in `docker-compose.yml` and
`dev.sh` prints its URL, but no code in this repository speaks SMTP — lark has
an SES provider and a stub that refuses, and nothing else. A queued message sits
at `pending` in `notify.messages` until something drains it, which locally is
nothing. This is [on the backlog](backlog.md); until it is fixed, read the link
out of the queue:

```bash
curl -s -X POST http://localhost:5050/api/auth/magic-link \
  -H 'content-type: application/json' -d '{"email":"them@example.com"}'

docker compose exec -T postgres psql -U arctic -d morganhacks -qAt -c \
  "SELECT substring(rendered_body_text from 'https?://[^[:space:]]*consume[^[:space:]]*')
     FROM notify.messages ORDER BY created_at DESC LIMIT 1;"
# http://localhost:3000/api/auth/consume?token=…
```

The response to the request is the same whether or not the address exists. That
is the point: otherwise the endpoint tells anyone who asks who applied.

The link points at port 3000, so `portalweb` has to be running to click it. The
`/api` prefix is deliberate — that is the path the portal proxies to harbor, and
without it the link lands on a Next.js 404 and the account looks broken rather
than the URL.

`scripts/try-login` was the guided version of this walkthrough. **It is
currently broken at step 7** and reports the failure as a missing database row,
which it is not — see the backlog. The steps above are what it was doing.

---

## Running the tests

```bash
cd src/atlas  && dotnet test Solution.slnx
cd src/harbor && dotnet test Solution.slnx
cd src/lark   && dotnet test Solution.slnx
```

The frontends have no test suites yet. What CI runs for each of them is a
typecheck and a build, so that is what to run:

```bash
cd src/portalweb   && npm run typecheck && npm run build
cd src/portaladmin && npm run typecheck && npm run build
cd src/portalforms && npm run typecheck && npm run build
```

CI runs the same commands, and only for the services a pull request touched.
If they pass here they pass there.

---

## Things that will confuse you once

**Two .NET installs will pick the wrong one.** If `dotnet build` says
*"The current .NET SDK does not support targeting .NET 10.0"*, you have both an
older x64 .NET and Homebrew's, and your shell is finding the older one first:

```bash
which -a dotnet          # whichever is listed first wins
dotnet --version         # must say 10.x
```

Put Homebrew ahead of it in `~/.zprofile`:

```bash
export PATH="/opt/homebrew/bin:$PATH"
```

On Apple Silicon the cleaner fix is removing the x64 install altogether — it
runs under Rosetta and buys nothing. `global.json` pins the requirement, so the
error names the version rather than pointing at a target framework.

**The solution file is `Solution.slnx`, not `.sln`.** .NET 10 creates the
newer XML solution format. `dotnet sln MorganHacks.sln` will tell you it cannot
find a solution; use the `.slnx` name or just `dotnet build` from `src/atlas`.

**A hidden `appsettings.json` is skipped, silently.** ASP.NET reads it through
a file provider that excludes anything macOS marks hidden — which is what a
checkout made inside a dot-directory ends up with, agent worktrees under
`.claude/` included. There is no error. harbor loads zero routes and answers a
body-less 404 to everything with nothing in the log; atlas falls back to its
compiled defaults, which look fine until a resume upload answers 503 because it
never read the Azurite connection string.

```bash
ls -lO src/harbor/MorganHacks.Harbor/appsettings.json   # "hidden" in the flags column
find src -name 'appsettings*.json' -exec chflags nohidden {} +
```

Worth knowing because it is not the cause the comments in those files name. They
warn that a single non-ASCII character stops the whole configuration binding,
and `src/harbor/MorganHacks.Harbor/appsettings.json` has two em-dashes in it
today while staging proxies correctly — so that is not the rule it is written
as. The hidden flag produces exactly the symptom described.

**`docker-entrypoint-initdb.d` only runs on an empty database.** If you change
`deploy/local/postgres/01-schemas.sql`, the change does nothing until you wipe
the volume:

```bash
docker compose down -v && docker compose up -d
```

**Postgres 18 moved its data directory.** The volume mounts at
`/var/lib/postgresql`, not `/var/lib/postgresql/data`. Mounting the old path
makes the container crash-loop with a message about `pg_upgrade`. Already
handled in `docker-compose.yml` — this note is here so nobody "fixes" it back.

**`.DS_Store` and iCloud.** This repo sits on a synced Desktop for at least one
of us, which drops `name 2.ext` duplicate files into build output. `tsconfig`
excludes `* 2.ts` for that reason.

---

## Where things live

```
src/atlas/        C#   the API. One service, several projects.
src/harbor/       C#   YARP gateway. The only thing published to the internet.
src/lark/         C#   email worker, no ingress
src/portalweb/    React public site and hacker portal
src/portaladmin/  React organizer console
src/portalforms/  React public forms, forms.morganhacks.com/<code>
libs/             shared code, not deployed on its own
deploy/local/     docker compose, dev.sh, sign-in.sh
deploy/azure/     Bicep and the deploy script for the backend
docs/             this, plus architecture, runbooks and the backlog
```

All six are built. atlas, harbor and lark run on Azure Container Apps and are
deployed; portalweb and portaladmin are Vercel projects and are deployed;
portalforms is a Vercel project whose builds are all being cancelled, so
`forms.morganhacks.com` currently serves nothing. That is the first item
[on the backlog](backlog.md).

atlas, harbor and lark have test suites. The frontends do not yet.

Inside `src/atlas`, every module has the same shape:

```
MorganHacks.Applications/
  Domain/              entities and value objects, no framework dependencies
  Data/                DbContext slice, repositories, entity configuration
  Services/            business logic
  IApplications.cs     the only surface other modules may use
```

Three rules keep those modules extractable into separate services later:

1. Only `MorganHacks.Api` references the modules. Modules never reference each
   other directly.
2. Cross-module calls go through the DI-wired root interface.
3. Each module owns its tables, and nobody else queries them.

Break those and this becomes a monolith pretending to be modular.

---

## Branches and deploying

| Push to | What happens |
|---|---|
| a feature branch | preview deployment on a generated URL |
| `staging` | the `*-stg` domains update |
| `main` | production updates, the backend deploys to staging, **and staging is reset to mirror main** |

To try a branch on staging without merging it anywhere:

```bash
scripts/claim-staging              # the branch you are on
scripts/claim-staging --release    # hand it back to main
```

Full detail in [`docs/architecture/deployments.md`](architecture/deployments.md).

---

## Sending real email

`lark` sends through SES, in staging and in production. Locally it sends
nothing at all — see [watching a hacker sign in](#watching-a-hacker-sign-in).

For staging and production, `lark` reads standard AWS environment variables:

```
AWS_REGION=us-east-1
AWS_ACCESS_KEY_ID=...
AWS_SECRET_ACCESS_KEY=...
```

With no region set it registers a provider that refuses and says so, and claims
nothing from the queue — so the backlog goes out untouched the moment
credentials arrive, rather than burning retry attempts on a problem no retry
fixes.

The region is **us-east-1**, and that is not arbitrary: production access is
granted per region, so it has to be the region the support case was raised in.
An identity verified in one region does nothing for another.

Two things gate real delivery, and they are separate:

- **Domain verification.** `auth.morganhacks.com`, verified in SES with DKIM,
  plus a custom MAIL FROM at `bounce.auth.morganhacks.com` so bounce reports
  come from our own subdomain rather than Amazon's. Done: DKIM and MAIL FROM
  both report SUCCESS in us-east-1.

  The MAIL FROM `MX` record names a region. Point it at the wrong one and SES
  reports the domain unverified with no useful explanation.
- **Leaving the sandbox.** In the sandbox SES accepts mail only for verified
  recipients. The code path is identical either way — a refused send is
  recorded as an ordinary failure — so everything can be built and tested
  before production access is granted.

A transactional subdomain separate from the broadcast one is the point rather
than decoration: a blast that collects spam complaints must not be able to take
sign-in links down with it.

### Bounce and complaint handling

SES publishes delivery events to an SNS topic, which posts them to
`https://<host>/api/webhooks/ses`. Subscribe that URL to the topic and the
endpoint confirms the subscription itself on the first request.

Point the topic at **staging** as well as production. A bounce arriving in
production for a message staging sent is indistinguishable from any other, and
splitting them is what keeps the two suppression lists honest.

Every request is verified against AWS's signing certificate before anything is
written. An unverified caller gets a 403 and no other information — this
endpoint writes to the suppression list, so an unauthenticated one would let
anybody stop an applicant receiving email, including their sign-in link.

Nothing to configure: verification uses the certificate AWS names in each
message, restricted to `sns.<region>.amazonaws.com`.

---

## Logs and error reporting

Every service writes structured JSON to stdout. Nothing reads these with eyes,
so they are shaped for an aggregator: `service`, `environment` and
`CorrelationId` are fields, not parts of a sentence.

```json
{"@t":"2026-09-01T04:36:01Z","service":"atlas","CorrelationId":"trace-abc-123"}
```

The correlation id starts at harbor, reaches atlas on a header, and is stamped
onto `notify.messages` so lark logs under it too — minutes later, in another
process. That chain is what turns "I never got my sign-in link" into one query.

Sentry is enabled by setting a DSN and stays off without one, so this all runs
locally with no accounts:

```
Sentry__Dsn=https://...@...ingest.sentry.io/...
Sentry__Release=<git sha>
```

Set `Sentry__Release` to the deployed SHA. Without it a spike in errors has to
be tied to a deploy by comparing timestamps.

**PII never leaves the process.** Sentry's own scrubbing knows about passwords
and card numbers; it does not know that `resume_key` points at somebody's CV or
that `responses` is a whole answer set. The list is ours, lives in
`libs/observability/Redaction.cs`, and covers log properties, Sentry extras,
tags, headers and message text. Request query strings and bodies are dropped
entirely — a magic-link token lives in a query string, and one captured in an
error report is a working sign-in sitting in an error tracker.

### The alert that matters is an absence

`magic_link.requested` staying healthy while `magic_link.consumed` collapses
means mail is not arriving. Every service is up, every dashboard is green, and
nobody can log in. No error rate catches it, because nothing is erroring. Both
are emitted as an `event` property on a log line, so an aggregator can count
them without a metrics stack to run.
