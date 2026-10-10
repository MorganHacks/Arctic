# Getting set up

Target: a clone to a running stack in **under 30 minutes**. If it takes you
longer, that is a bug in this document — open an issue.

---

## What you need first

Five tools, and `dev.sh` refuses to start without any of them. The list is not
a recommendation -- it is the loop the script actually runs, so if you have
these it will get past its checks.

| | Version | Check | Install on macOS |
|---|---|---|---|
| **Docker** | any recent | `docker info` | `brew install --cask docker` |
| **.NET SDK** | **10.x** | `dotnet --list-sdks` | `brew install dotnet` |
| **Node** (gives you npm) | **24 or newer** | `node -v` | `brew install node` |
| **curl** | any | `curl --version` | already there |
| **openssl** | any | `openssl version` | already there |

On Linux, install Docker Engine from docker.com, .NET from
`packages.microsoft.com`, and Node from NodeSource or your package manager. On
Windows, use WSL2 and follow the Linux instructions inside it -- `dev.sh` is a
shell script and expects a Unix shell.

Everything above in one line, if you have Homebrew:

```bash
brew install dotnet node && brew install --cask docker
```

### Docker has to be running, not just installed

`dev.sh` checks this separately from whether the command exists, because
installing Docker Desktop and never opening it is the most common way to fail
here. Open the app and wait for the whale to settle before you run anything.

```bash
docker info >/dev/null 2>&1 && echo running || echo "start Docker Desktop"
```

### Five ports have to be free

`dev.sh` checks these too and stops rather than fighting whatever already
holds one: **5080** (atlas), **5050** (harbor), **3000** (portalweb), **3001**
(portaladmin), **3002** (portalforms).

```bash
lsof -ti tcp:5080,5050,3000,3001,3002        # anything listed is in the way
lsof -ti tcp:3000 | xargs kill               # how to clear one
```

### On the Node version

CI builds on Node 24 (`.github/workflows/build-node.yml`), and there is no
`.nvmrc`, so nothing pins your local version. Newer works -- 26 is in use --
but 24 is the one the build is actually verified against, so prefer it if you
are choosing.

### What you do not need for local development

`gh`, `az`, `aws` and `vercel` are for deploying and operating the system, not
for running it. Nothing in `dev.sh` touches them. Install them when you reach
`deploy/azure/README.md` or the runbooks, and not before -- a local stack needs
no cloud account at all.

---

## 1. Start the whole thing

```bash
git clone https://github.com/Morgan-Hacks/Arctic.git
cd Arctic
deploy/local/dev.sh you@morgan.edu
```

That is the whole setup. The script checks its tools, brings up the containers,
waits for Postgres, applies migrations, seeds the address you gave it as a super
admin, starts the five services, and opens the organizer console with you
already signed in. Ctrl+C stops everything it started.

| | Where | |
|---|---|---|
| Postgres | `localhost:5432` | one database, one schema per module |
| Azurite | `localhost:10000` | Azure Blob emulator; resumes land here |
| Mailpit | `localhost:8025` | see [watching a sign-in](#watching-a-hacker-sign-in) before you wait on this |
| atlas | `localhost:5080` | the API |
| harbor | `localhost:5050` | the gateway — **not optional**, see below |
| portalweb | `localhost:3000` | the marketing site and the hacker portal |
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

`dev.sh` starts `portalweb` along with everything else. It is the marketing
site and the hacker portal, and neither is needed to work on forms or on the
console — if that is all you are doing, the four terminals above are enough
and you can leave this one closed. Started on its own:

```bash
cd src/portalweb
npm install
npm run dev          # http://localhost:3000
```

It reaches the API the same way the others do — its own origin, rewritten to
harbor.

To see what an applicant sees rather than what `dev.sh` already signed you in
as, the same development door works through this origin too, because it
proxies `/api/*` the same way portaladmin does:

```
http://localhost:3000/api/dev/sign-in?email=THEIR@ADDRESS&next=/portal
```

The address has to belong to an application that already exists — the door
authenticates whoever you tell it to, it does not create them. See
[watching a hacker sign in](#watching-a-hacker-sign-in) for getting one into
the database in the first place.

Emailed sign-in links are built from `PublicBaseUrl` on atlas, which defaults to
`http://localhost:3000` and therefore needs nothing set locally. In a deployed
environment it is the portal's real origin, set from `PUBLIC_BASE_URL`.

A sign-in link for a **form** lands somewhere else: `FormsBaseUrl`, which
defaults to `http://localhost:3002` — the port `dev.sh` starts `portalforms` on
— and is set from `FORMS_BASE_URL` when deployed. It has to be its own setting
because the session cookie is host-only: a link that lands on the portal sets a
cookie the forms site is never sent, so the person arrives at the form still
signed out while their browser holds a perfectly good session for a different
hostname.

A third, `ConsoleBaseUrl`, is where the **organizer console** is: it defaults to
`http://localhost:3001` and is set from `CONSOLE_BASE_URL` when deployed. Only
one email uses it — the welcome a new organizer gets when they join their first
team — so an unset value in a deployed environment is not an outage, it is a
link in that email pointing at a machine nobody is running.

---

## Google sign-in for organizers

Optional locally, because the development door above exists. Without credentials
`/auth/google` answers 503 and everything else still works, so nobody needs a
Google project to develop the rest.

```bash
export Google__ClientId=...apps.googleusercontent.com
export Google__ClientSecret=...
export Google__RedirectUri=http://localhost:3001/api/auth/google/callback
```

Through portaladmin's own origin, with the `/api` prefix — not straight at
atlas. The sign-in button on the console links to `/api/auth/google` on
`:3001`, and the PKCE state cookie that round trip sets is scoped to that same
host and path; sending Google's callback anywhere else means the cookie never
comes back and the callback has nothing to check the state against. The
client's authorized redirect URI in Google Cloud Console has to match this
exactly.

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

## Every setting, in one place

Nothing in this section is needed to run the thing locally — `deploy/local/dev.sh`
starts everything with working defaults, and that is the point of the defaults.
This is the list for when you are deploying somewhere, or wondering why a thing
that works on your laptop does not work in staging.

.NET reads nested keys from environment variables with a double underscore, so
`Google:ClientId` is `Google__ClientId` in a container and `Google:ClientId` in
a JSON file. Both spellings appear below because both appear in real life.

### atlas — the API

| Setting | Env var | Needed | If unset |
|---|---|---|---|
| `ConnectionStrings:Postgres` | `ConnectionStrings__Postgres` | **always** | will not start |
| `Google:ClientId` | `Google__ClientId` | for organizer sign-in | `/auth/google` answers 503 |
| `Google:ClientSecret` | `Google__ClientSecret` | for organizer sign-in | as above |
| `Google:RedirectUri` | `Google__RedirectUri` | deployed | `http://localhost:3000/api/auth/google/callback` |
| `PublicBaseUrl` | `PublicBaseUrl` | deployed | `http://localhost:3000` — emailed sign-in links point at a machine nobody is running |
| `FormsBaseUrl` | `FormsBaseUrl` | deployed | `http://localhost:3002` |
| `ConsoleBaseUrl` | `ConsoleBaseUrl` | deployed | `http://localhost:3001` — only the organizer welcome email uses it |
| `Network:ProxySecret` | `Network__ProxySecret` | behind a proxy | forwarded client addresses are not believed, so every rate limit buckets on the proxy |
| `Network:ForwardLimit` | `Network__ForwardLimit` | rarely | framework default |
| `Resumes:ConnectionString` | `Resumes__ConnectionString` | locally | set in `appsettings.Development.json` to Azurite's published dev values |
| `Resumes:AccountName` | `Resumes__AccountName` | deployed | uploads fail; deployed environments use this plus a managed identity instead of a key |
| `Resumes:ClientId` | `Resumes__ClientId` | deployed | which managed identity to authenticate as |
| `Resumes:Container` | `Resumes__Container` | no | `resumes` |
| `Sentry:Dsn` | `Sentry__Dsn` | no | errors are logged and not reported |

### lark — the mail sender

| Setting | Env var | Needed | If unset |
|---|---|---|---|
| `ConnectionStrings:Postgres` | `ConnectionStrings__Postgres` | **always** | will not start |
| — | `AWS_REGION` | to send | the send loop logs that no provider is configured and claims nothing |
| — | `AWS_ACCESS_KEY_ID` | to send | as above |
| — | `AWS_SECRET_ACCESS_KEY` | to send | as above |
| `SendLoop:*` | `SendLoop__BatchSize` and friends | no | 25 per batch, 5s idle, 140ms between sends |

Queued mail is not lost while credentials are missing. The loop claims nothing,
the rows stay pending, and everything goes out untouched once the credentials
arrive.

### harbor — the proxy

| Setting | Env var | Needed | If unset |
|---|---|---|---|
| `ReverseProxy:*` | — | **always** | comes from `appsettings.json`; this is the route allowlist |
| `Cors:Origins` | `Cors__Origins` | deployed | browsers refuse cross-origin calls |
| `Network:KnownProxies` | `Network__KnownProxies` | deployed | forwarded headers ignored |
| `Network:KnownNetworks` | `Network__KnownNetworks` | deployed | as above |

A non-ASCII character anywhere in harbor's `appsettings.json` stops the whole
configuration binding, silently. There is a test that fails on it.

### The three front ends

| Env var | Which | Needed | If unset |
|---|---|---|---|
| `API_ORIGIN` | all three | deployed | `http://localhost:5050` |
| `PORT` | all three | locally | Next's default 3000, which collides |
| `PROXY_SHARED_SECRET` | all three | behind a proxy | the client address is not forwarded, so atlas rate-limits everybody into one bucket |
| `NEXT_PUBLIC_FORMS_ORIGIN` | portaladmin | deployed | form links are built against a hardcoded production URL |

All front ends read and write through the API in development and production.
There are no fixture modes or successful-write fallbacks.

### Feature flags

A flag named in `features.json` is overridden by an environment variable of the
same name shouted: `ENABLE_HACKER_PORTAL_FEATURE=true`. The file is inserted as
the **lowest** priority source, so the variable always wins.

Unset is not the same as `false`. Unset means the file decides; `false` means
you decided.

### What the deploy workflow expects

`deploy-azure.yml` reads these from GitHub. Repository **secrets**:
`DB_PASSWORD`, `SENTRY_DSN`, `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`,
`GOOGLE_CLIENT_SECRET`, `PROXY_SHARED_SECRET`.

Environment **variables** (set per environment — Staging and Production have
their own): `SUPER_ADMIN_EMAIL`, `AWS_REGION`, `GOOGLE_CLIENT_ID`,
`GOOGLE_REDIRECT_URI`, `PUBLIC_BASE_URL`, `FORMS_BASE_URL`, `CONSOLE_BASE_URL`,
`WARM_REPLICAS`, `ENABLE_HACKER_PORTAL_FEATURE`, `ENABLE_CHECK_IN_DESK`.

Staging has all of these. **Production has none of the secrets**, which is the
one reason a production deploy would fail today — there is no production
environment in Azure either, only `rg-mh-staging`.

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

Worth knowing because it produces the identical symptom to a different bug.
`appsettings.json`'s own comments warn that a single non-ASCII character
anywhere in the file also stops the whole configuration binding, silently,
with the same result: zero routes, a body-less 404 to everything. That file is
plain ASCII today for exactly that reason, so if you hit the zero-routes
symptom, the hidden flag is the one to check first — a stray non-ASCII
character is not sitting there waiting to be found.

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
AWS_REGION=us-east-2
AWS_ACCESS_KEY_ID=...
AWS_SECRET_ACCESS_KEY=...
```

With no region set it registers a provider that refuses and says so, and claims
nothing from the queue — so the backlog goes out untouched the moment
credentials arrive, rather than burning retry attempts on a problem no retry
fixes.

The region is **us-east-2**, and that is not arbitrary: production access is
granted per region, so it has to be the region the support case was raised in.
An identity verified in one region does nothing for another.

Two things gate real delivery, and they are separate:

- **Domain verification.** `auth.morganhacks.com`, verified in SES with DKIM,
  plus a custom MAIL FROM at `bounce.auth.morganhacks.com` so bounce reports
  come from our own subdomain rather than Amazon's. Done: DKIM and MAIL FROM
  both report SUCCESS in us-east-2.

  The MAIL FROM `MX` record names a region. Point it at the wrong one and SES
  reports the domain unverified with no useful explanation.
- **Leaving the sandbox.** In the sandbox SES accepts mail only for verified
  recipients. The code path is identical either way — a refused send is
  recorded as an ordinary failure — so everything can be built and tested
  before production access is granted.

A transactional subdomain separate from the broadcast one is the point rather
than decoration: a blast that collects spam complaints must not be able to take
sign-in links down with it.

### Campaign engagement tracking

Choose **Enable email tracking** when creating a campaign. The choice defaults
to the selected template's tracking setting and is saved on the campaign, so
later template edits do not change it. It controls both link redirects and a
one-pixel open image. Transactional emails use the template setting.
Set `SendLoop__ClickTrackingBaseUrl` on lark to the public API URL, including the
`/api` prefix when using Harbor. Both `/email/click/{token}` and
`/email/open/{token}` must reach atlas without authentication or edge caching.

Apply migrations `0041_email_engagement.sql`, `0042_campaign_recipient_names.sql`,
`0043_campaign_tracking.sql`, and `0044_campaign_settings.sql` before deploying atlas or lark. Deploy Harbor's
open-tracking route with the API update, then deploy portaladmin.
Existing link totals remain available. Individual click history begins after
the API update; opens require emails prepared by the updated worker. There is
no historical backfill for opens, devices, or event timestamps.

The campaign report separates messages accepted for sending from the provider's
current delivery statuses. Open rate is unique opened messages divided by sent
messages carrying an open image. Click rate is unique clicked messages divided
by sent messages with tracked links. Daily and hourly charts cover the last
90 days in UTC; totals and device breakdowns cover all recorded activity.
Security scanners and image proxies can generate events, while blocked images
can hide opens. These are engagement signals, not proof that a person read the
email. Browser, operating system, platform, and country reports use click events.

The atlas image includes DB-IP's country database and sets
`EmailTracking__CountryDatabasePath`. Country lookup runs locally against the
client address resolved by the existing trusted-proxy configuration. No request
is sent to a geolocation service. The report includes the required
[DB-IP attribution](https://db-ip.com/db/download/ip-to-country-lite).
Refresh `COUNTRY_DATABASE_RELEASE` and `COUNTRY_DATABASE_SHA256` in the atlas
Docker build when adopting a new monthly database release. The checksum is for
the compressed download; verify the decompressed database against the checksum
published by DB-IP. For local development, point
`EmailTracking__CountryDatabasePath` at a downloaded `.mmdb` file. Loopback and
unresolved addresses remain unknown.

An installation with its own trusted country-enriching ingress can instead set
`EmailTracking__CountryHeader`, provided that ingress overwrites client values
and cannot be bypassed. Do not enable that override on an unrestricted public
host. Only the country code, parsed device categories, event time, and automation
flag are stored; raw IP addresses and user-agent strings are not retained by
tracking.

Campaign recipients are read from saved message rows, ten per page. Names are
snapshotted when the campaign is queued; missing names on older messages remain
blank. The date column is the actual send time, not a generated activity date.

Viewing counts requires `email.view_stats`; email content, recipient pages,
and link destinations also require `email.manage_templates`. Loading the
report does not send email or resolve a new audience.

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

## Mock applicants for organizer simulations

### Local testing

After starting the local stack, run `deploy/local/seed-hackers.sh --apply` from
another terminal at the repository root. This adds 50 fictional applicants to a
separate **MOCK — Organizer simulation** event in the local organizer console.
Run without `--apply` for a preview. Reruns preserve existing applicants and your
review changes.

### Shared staging testing

Once the workflow is merged into `main`, open GitHub → **Actions → Seed staging
applicants → Run workflow**. Select `main`, choose a total count (50 by default),
and check **Create applicants** to write the data. Leave it unchecked for a preview
that validates configuration without connecting to the database.

After the run succeeds, sign in at
[admin-stg.morganhacks.com](https://admin-stg.morganhacks.com/applicants?event=028c6934-b837-44e4-9b40-076083d126ae)
and select **MOCK — Organizer simulation**. The team shares this staging dataset;
local seed data is not copied there. Existing staging organizer access is required.

Merging does not automatically seed staging. This manual action uses the existing
Staging environment's Azure credentials and database secret, requires the deployed
staging schema, and blocks production. Mock-address suppressions prevent real email
delivery during reviews. Reruns preserve notes and decisions.

See [the seed utility guide](../src/atlas/MorganHacks.Seed/README.md) for prerequisites,
counts, safeguards, and tests.
