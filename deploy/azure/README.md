# Azure Container Apps

Bicep describes what should exist. Two small scripts do the parts that are
genuinely imperative: building images, and running the migration job.

```bash
az login                      # into the MorganHacks account, not a personal one

export DB_PASSWORD=$(openssl rand -base64 32 | tr -d '/+=' | head -c 32)
export SUPER_ADMIN_EMAIL=olola73@morgan.edu

az group create -n morganhacks-shared  -l eastus
az group create -n morganhacks-staging -l eastus
az deployment group create -g morganhacks-shared -f deploy/azure/shared.bicep \
    --parameters registryName=morganhacksacr

./deploy/azure/push-images.sh staging
./deploy/azure/deploy.sh      staging              # what-if — changes nothing
./deploy/azure/deploy.sh      staging --apply
```

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

## Why two templates and not one

`platform.bicep` creates Postgres, the environment and the migration job.
`apps.bicep` creates the three services.

One template would update the job and the services in the same deployment,
which puts new code in front of an old schema for however long the job takes.
That window is where every migration bug lives. `deploy.sh` runs them in order
and **stops if migrations fail** — not deploying is better than deploying onto
a schema the code does not expect.

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

## Not in the templates

**`Network__KnownNetworks`.** Container Apps terminates in front of harbor, so
until this names it, `RemoteIpAddress` is the platform's and every per-IP rate
limit shares one bucket for the whole internet. It needs the environment's
subnet, which does not exist until the environment does.
`docs/architecture/deployments.md` has the reasoning and how to check it.

**A private endpoint for Postgres.** The firewall rule currently allows Azure
services, which is the weakest thing in here. VNet integration with a private
endpoint is the upgrade.

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
