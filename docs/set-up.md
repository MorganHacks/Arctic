# Getting set up

Target: a clone to a running API in **under 30 minutes**. If it takes you
longer, that is a bug in this document — open an issue.

---

## What you need first

| | Version | Check with |
|---|---|---|
| .NET SDK | **10.x** | `dotnet --list-sdks` |
| Node | **24.x** | `node -v` |
| Docker | any recent | `docker info` |

.NET 10 rather than 8: .NET 8 leaves support in November 2026, roughly when
this platform would be running.

---

## 1. Clone and start the local stack

```bash
git clone https://github.com/MorganHacks/Arctic.git
cd Arctic
docker compose up -d
```

That gives you four things:

| Service | Where | What it is |
|---|---|---|
| Postgres | `localhost:5432` | one database, one schema per module |
| MinIO | `localhost:9000`, console on `:9001` | S3-compatible storage, standing in for Cloudflare R2 |
| Mailpit | SMTP on `:1025`, UI on `:8025` | catches every email so nothing leaves your machine |

Credentials are `arctic` / `local-dev-only` everywhere. They are meant to be
boring and public — nothing here should ever hold real data.

Check it came up:

```bash
docker compose ps
```

All three should say `healthy`.

---

## 2. Apply the schema

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

The connection string comes from `ARCTIC_DB`, defaulting to the local
compose stack.

---

## 3. Run the API

```bash
cd src/atlas
dotnet build Solution.slnx
dotnet run --project MorganHacks.Api --urls http://localhost:5080
```

Then:

```bash
curl http://localhost:5080/health
# {"status":"ok"}
```

That is the whole check. `/health` is liveness only and deliberately does not
touch the database — a Postgres blip that restarts every pod turns a
recoverable problem into an outage.

---

## 4. Run the public site

```bash
cd src/portalweb
npm install
npm run dev          # http://localhost:3000
```

---

## Running the tests

```bash
cd src/atlas
dotnet test Solution.slnx
```

```bash
cd src/portalweb
npm run typecheck
npm run build
```

CI runs the same commands. If they pass here they pass there.

---

## Things that will confuse you once

**The solution file is `Solution.slnx`, not `.sln`.** .NET 10 creates the
newer XML solution format. `dotnet sln MorganHacks.sln` will tell you it cannot
find a solution; use the `.slnx` name or just `dotnet build` from `src/atlas`.

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
src/lark/         C#   email worker, no ingress          (not built yet)
src/harbor/       C#   YARP gateway                      (not built yet)
src/portalweb/    React public site and hacker portal
src/portaladmin/  React organizer console                (not built yet)
libs/             shared code, not deployed on its own
deploy/local/     everything docker compose needs
docs/             this, plus architecture and runbooks
```

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
| `staging` | `main-stg.morganhacks.com` updates |
| `main` | production updates, and nothing else does |

Full detail in [`docs/architecture/deployments.md`](architecture/deployments.md).
