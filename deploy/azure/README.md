# Azure Container Apps

Three scripts, in order. All of them are safe to re-run.

```bash
az login

export DB_PASSWORD=$(openssl rand -base64 32 | tr -d '/+=' | head -c 32)
./deploy/azure/provision.sh staging

# once, against the new server
psql "$ARCTIC_DB" -f deploy/local/postgres/01-schemas.sql

./deploy/azure/push-images.sh  staging $(git rev-parse --short HEAD)
./deploy/azure/deploy-apps.sh  staging $(git rev-parse --short HEAD)
```

## Why scripts and not the portal

The portal is good for looking at things and bad for creating them. At some
point staging needs rebuilding — a bad migration, a wrong region, a resource
group deleted by someone tidying up — and "click through forty screens the way
you did in September" is not a recovery procedure.

A script is also reviewable. A change to how production is provisioned arrives
as a diff in a pull request rather than as something that already happened.

Use the portal freely for reading: logs, metrics, what a revision is doing.

## What gets created

| Resource | Why |
|---|---|
| `morganhacks-shared` / registry | One registry for every environment, so staging and production run identical bytes rather than two builds of one commit |
| Container Apps environment | The shared network and log destination |
| Postgres flexible server, Burstable B1ms | Managed, because self-hosting the database is the one mistake that is permanent |
| `harbor` | The only thing with an external ingress |
| `atlas` | Internal ingress — harbor is the only path in |
| `lark` | No ingress at all |
| `migrations` | A job, not an app |

## Two decisions worth knowing

**Migrations block the deploy.** The job runs first and the services are not
updated unless it succeeds. An API that migrates on startup means every replica
racing to alter one schema.

**`lark` runs at one replica, never zero.** It has no ingress, so nothing would
ever wake it, and a queue with no worker is a queue that silently stops
sending — with everything green.

## Images are tagged by commit

Never `:latest`. A rollback is re-deploying a tag that already exists, rather
than rebuilding and hoping you get the same thing.

Builds are `--platform linux/amd64` explicitly. Container Apps runs amd64 and a
Mac builds arm64 by default; the failure is an image that works perfectly on
the laptop that built it and crash-loops in the cloud.

## After the first deploy

Two settings are not in these scripts because they need values that only exist
once the environment does:

- **`Network__KnownNetworks`** — Container Apps sits in front of harbor, so
  until this names it, `RemoteIpAddress` is the platform's and every per-IP
  rate limit shares one bucket for the entire internet. Reasoning and how to
  check it are in `docs/architecture/deployments.md`.
- **`Sentry__Dsn`** and **`Sentry__Release`** — off until set.

## On the subscription

A Visual Studio subscription's monthly credit is **for development and testing
only** under its terms. That is fine for staging and is not a licence to run
registration on it. Production needs either a pay-as-you-go subscription or the
sponsorship credits described in `doc-starter/morganhacks-microsoft-sponsorship.md`.
