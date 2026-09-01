# Azure Container Apps

Bicep describes what should exist. Two small scripts do the parts that are
genuinely imperative: building images, and running the migration job.

```bash
az login                      # into the MorganHacks account, not a personal one

export DB_PASSWORD=$(cat ~/.mh-staging-db-password)
export SUPER_ADMIN_EMAIL=olola73@morgan.edu

./deploy/azure/deploy.sh staging              # what-if — changes nothing
./deploy/azure/deploy.sh staging --apply
```

`deploy.sh` builds and pushes the images itself, because the registry has to
exist before anything can be pushed to it. `SKIP_PUSH=1` skips that step, which
is what a rollback wants — the tag already exists and rebuilding it would
produce different bytes from the ones being rolled back to.

Optional, and each one is off when unset rather than half-configured:

```bash
export SENTRY_DSN=...                         # error reporting
export AWS_REGION=us-east-1                   # lark sends only when these are set
export AWS_ACCESS_KEY_ID=...
export AWS_SECRET_ACCESS_KEY=...
```

Resource groups included. `main.bicep` is subscription-scoped and owns them,
so an environment is one thing that either exists or does not rather than a
group somebody has to remember to create first.

Keep `DB_PASSWORD` somewhere real. Bicep never reads it back, so losing it
means resetting the admin password on the server.

## Why Bicep and not a shell script

A script has to be told *how* to reach the desired state, and gets idempotency
only where somebody hand-rolled it. Bicep describes the state, so re-deploying
converges — including correcting anything changed by hand in the portal.

The property worth the most is `what-if`. An infrastructure change can be
reviewed before it happens, the same way a code change is. That is the same
argument that rules out clicking through the portal, taken one step further:
a change to how production is provisioned should arrive as a diff.

## Layout

```
main.bicep              the front door — resource groups, then the modules
naming.md               naming convention and what every tag is for
modules/registry.bicep  the container registry, shared across environments
modules/platform.bicep  Postgres, the apps environment, the migration job
modules/apps.bicep      harbor, atlas, lark
staging.bicepparam      per-environment values
prod.bicepparam
deploy.sh               the sequence Bicep cannot express
push-images.sh
```

Values live in the `.bicepparam` files, which are type-checked against
`main.bicep` — a wrong or missing parameter fails at compile time rather than
half way through a deployment. Secrets are read from the environment with
`readEnvironmentVariable`, so these files are safe in the repository and there
is one fewer place a password gets committed by accident.

## Why it deploys in two passes

`deployApps` is false on the first pass. Migrations run between the two.

One pass would update the migration job and the services in the same
deployment, which puts new code in front of an old schema for however long the
job takes — and that window is where every migration bug lives. `deploy.sh`
runs platform, then migrations, then apps, and **stops if migrations fail**.
Not deploying beats deploying onto a schema the code does not expect.

This is the one thing Bicep cannot express: "run this and wait for it" is a
sequence, not a state.

## Shape of it

| | Ingress | Replicas |
|---|---|---|
| `harbor` | external — the only thing published | 1–3 |
| `atlas` | internal — harbor is the only path in | 1–3 |
| `lark` | none at all | 1, never 0 |
| `migrations` | a job | on demand |

`lark` never scales to zero. It has no ingress, so nothing would wake it, and a
queue with no worker is a queue that silently stops sending while every
dashboard reads green.

The registry lives in its own resource group so deleting an environment cannot
take the images with it — including the image a rollback needs.

## Rolling back

```bash
./deploy/azure/deploy.sh staging --apply <older-tag>
```

Images are tagged by commit, never `:latest`, so this re-deploys bytes that
already exist rather than rebuilding and hoping. Note it re-runs migrations —
which is fine forward, and is why a migration that drops something needs a
second thought.

## Pulling images

Services pull with a **user-assigned managed identity** granted `AcrPull` on
the shared registry, one identity per environment. The registry has no admin
user.

The alternative was the registry's admin password: one static credential shared
by every service, readable by anyone with access to the resource, and rotating
it means redeploying everything at once. The identity is scoped to its
environment, grants exactly pull, and has nothing to leak.

Pushing still uses your own `az login`. Push belongs to a person or to CI, not
to a running service.

On a first deploy the role assignment occasionally has not propagated by the
time the apps start, and they fail to pull. Re-running `deploy.sh` fixes it —
the templates are idempotent and the grant is already there by then.

## Not in the templates

**`Network__KnownNetworks`.** Container Apps terminates in front of harbor, so
until this names it, `RemoteIpAddress` is the platform's and every per-IP rate
limit shares one bucket for the whole internet. It needs the environment's
subnet, which does not exist until the environment does.
`docs/architecture/deployments.md` has the reasoning and how to check it.

**A private endpoint for Postgres.** The firewall rule allows Azure services,
which is now the weakest thing here: any Azure tenant's resources can reach the
server, though they still need the password. The fix is VNet integration with a
private endpoint, and it means recreating the Container Apps environment —
VNet cannot be added to an existing one. Worth doing before production carries
real applicant data; not worth rebuilding staging for on its own.

## On subscriptions

A Visual Studio subscription's monthly credit is **for development and testing
only** under its terms. Fine for staging; not a licence to run registration on
it, and being cut off during registration week is the worst version of that
mistake.

Production needs a pay-as-you-go subscription or the sponsorship credits in
`doc-starter/morganhacks-microsoft-sponsorship.md`. That doc also flags two
shorter paths worth trying first: whether Morgan State already has an Azure
education subscription, and — if MorganHacks is MLH-affiliated — that MLH has
existing Azure relationships and credits flow through that channel routinely.

**Own these resources with the MorganHacks account, not a personal one.** Same
rule as the Vercel projects and `tech@morganhacks.com`: infrastructure tied to
somebody's student account is infrastructure that leaves when they graduate.

## Four things the first real deploy found

Worth recording, because none of them show up in a test.

**`eastus` is capacity-restricted on this subscription.** Postgres provisioning
is refused there outright, and the error is `Version should be in: []` rather
than anything about capacity. `centralus` is open and offers Postgres 18, which
is what docker-compose and the tests run — so local and deployed now match
exactly.

**`citext` has to be allow-listed on the server.** Azure refuses `CREATE
EXTENSION` regardless of the connecting user's privileges until the extension
is named in `azure.extensions`. Without it the notify schema cannot be created
at all.

**A subscription deployment records its location and will not move.** Changing
region means deleting the record first:
`az deployment sub delete -n arctic-<env>-platform`.

**Container Apps rejects a secret with an empty value.** "Off unless
configured" therefore has to mean the secret is absent, not blank — otherwise
the thing that lets this run with no accounts is the thing that stops it
deploying.

## Deploys run in CI, not on a laptop

`.github/workflows/deploy-azure.yml` is what actually deploys.

| Trigger | Goes to |
|---|---|
| push to `staging` | staging, no approval |
| push to `main` | production, after a required review |
| manual dispatch | either, and takes a tag — an older tag is a rollback |

Running `deploy.sh` by hand still works and is the right tool when something is
broken. It is not how a normal deploy should happen: a deploy that depends on
one person's machine depends on that person being awake, having the tools, and
being logged into the right account, and it leaves no record of what shipped or
who shipped it.

### There is no Azure credential in GitHub

Authentication is OIDC. The workflow proves which repository and which
environment it is running as, and Azure trusts that exact pair through a
federated credential on `id-mh-deploy`. Nothing is stored that could leak,
because nothing is stored.

What the deploy identity may do:

| Grant | Scope | Why |
|---|---|---|
| Contributor | subscription | creates the resource groups and everything in them |
| User Access Administrator | `rg-mh-shared` only | grants AcrPull to each environment's pull identity |
| AcrPush | the registry | pushes images; Contributor does not cover data-plane push |

### Configuration

Repository variables, because none of them are secrets: `AZURE_CLIENT_ID`,
`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `SUPER_ADMIN_EMAIL`.

Environment secrets, set per environment: `DB_PASSWORD`, and later
`SENTRY_DSN`, `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`.

**Production deliberately has no `DB_PASSWORD` yet.** There is no production
database, and it must not inherit staging's. Leaving it unset makes creating
one a deliberate act rather than something that happens by copying.
