# Organizer simulations: local and staging

Create fake applicants for reviewing, filtering, decisions, and analytics in the
**local** or **staging** organizer console. Production remains blocked.

## Local use

Start the normal local stack first, including migrations and your organizer login:

```bash
deploy/local/dev.sh you@morgan.edu
```

In another terminal at the repository root:

```bash
# Preview only: no database connection or changes.
deploy/local/seed-hackers.sh

# Create 50 applicants.
deploy/local/seed-hackers.sh --apply

# Add more, keeping the first 50 and your review changes.
deploy/local/seed-hackers.sh --count 100 --apply
```

Open the Applicants page on `http://localhost:3001` and choose
**MOCK — Organizer simulation**. The command also prints a direct link.

## What it creates

- One separate mock event and a published application form.
- Up to 1,000 deterministic `mock-organizer-NNNN@example.com` applicants.
- All 11 lifecycle statuses when the count is at least 11, including drafts,
  accepted, waitlisted, confirmed, and checked in.
- Varied names, schools, majors, shirt sizes, dietary answers, and short essays.
- Real validation, column/JSON answer mapping, transitions, and status history.
- Submission dates spread over the previous month for the analytics charts.

Draft applicants intentionally have only an email. Consent fields are synthetic
fixtures for fictional applicants, not records of real consent. Reviewers may add
notes and change statuses normally. The initial history has a null actor and a
synthetic-data reason for reviewer transitions; no real organizer is impersonated.

The seed does not create login identities, resume files, or email messages. It does
not test the hacker portal. Before inserting each applicant, it adds a manual
email suppression for that mock address. This blocks both transactional and
broadcast delivery, including emails triggered by subsequent organizer decisions.
Existing unrelated addresses are unaffected.

## Reruns and failures

An existing application with the same mock event and email is skipped. Its answers,
status, dates, and notes are preserved. Lowering `--count` never deletes applicants.
The form is not overwritten once published; incompatible edits make a run fail.

Completed rows survive a failed run. A handled failure removes only the newly
created application that failed, so rerunning can retry it. A process killed midway
may leave that one row partially prepared; reruns preserve it like any other
existing application. There is intentionally no destructive reset command.

## Local safeguards

The launcher verifies a local Docker socket, the Compose Postgres container, and
its Postgres cluster identifier. The utility compares that identifier with the
server at `127.0.0.1:5432` before writing. A localhost production tunnel is not
accepted merely because its hostname looks local.

The connection is fixed to the repository's local development database and
credentials. `ARCTIC_DB` must be unset, `ARCTIC_TARGET` must be unset or `local`, and
.NET environment settings must be unset or `Development`. No endpoint or database
connection override is supported. Explicit `--apply` is required. The reserved
mock event has a fixed UUID and slug; conflicting identifiers are rejected.

These are safeguards against accidental targeting, not a security boundary against
someone editing the utility or supplying its internal verification argument.

The utility is a standalone console project. It is not referenced by the API or
migrations and never runs on application startup. It changes no schema. It calls
application stores directly, bypassing endpoint email orchestration.

## Shared staging simulations

After this workflow is merged into `main`, open GitHub → **Actions → Seed staging
applicants → Run workflow**. Choose `main`, leave the count at 50 (or enter 1–1000),
and first leave **Create applicants** unchecked to preview. This preview validates
the configuration without connecting to or writing to the database; it still builds
the image and configures the Azure job. To populate staging, run it again with
**Create applicants** checked.

Wait for the action to succeed, then open
[the staging organizer console](https://admin-stg.morganhacks.com/applicants?event=028c6934-b837-44e4-9b40-076083d126ae)
and choose **MOCK — Organizer simulation**. Everyone with the appropriate staging
organizer permissions sees the same applicants. This does not create organizer
accounts; use the existing staging sign-in process.

Merging alone does not seed any database. This is a separate, manually launched
action. It uses the existing GitHub **Staging** environment's Azure OIDC variables
(`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`) and `DB_PASSWORD`
secret, plus optional `REGISTRY_NAME`. The existing staging infrastructure and
schema migrations must already be deployed. Environment approval rules still apply.

The action builds a standalone image, provisions `caj-seed-staging` in
`rg-mh-staging`, and waits for that exact execution to finish. It shares the staging
deployment lock so migrations cannot overlap. The Azure identity needs permissions
to build in the registry and deploy/run this job, in addition to the existing
staging resources. If Azure denies a step, an infrastructure maintainer must grant
the missing access; no production credentials should be substituted.

The job requires explicit staging environment settings and allows only
`psql-mh-staging.postgres.database.azure.com:5432/morganhacks`, with full TLS
certificate verification. There is no production target input. Local mode retains
its Docker-cluster verification. Mock addresses are suppressed before applicants
become visible; retain those suppressions while using the mock event.

Rerunning preserves review decisions and notes and can extend the requested count.
There is no reset/delete option. This first version intentionally uses its own mock
form/event rather than changing the real event's questions or applicants.

## Verification

```bash
# Validation and safety tests, without Docker.
dotnet test src/atlas/MorganHacks.Seed.Tests --filter 'Category!=Database'

# Includes a disposable Postgres 18 database; requires Docker.
dotnet test src/atlas/MorganHacks.Seed.Tests
```

The database test applies the real migrations and checks all statuses, submission
dates, reruns after a manual decision, preservation of an unrelated application,
and an empty email queue. It never uses the regular local database.
