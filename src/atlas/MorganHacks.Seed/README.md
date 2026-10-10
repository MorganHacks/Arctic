# Local organizer simulations

Create fake applicants for reviewing, filtering, decisions, and analytics in the
**local** organizer console. This does not add applicants to admin.morganhacks.com.

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

This first version does not create login identities, resume files, or email
messages. It does not test the hacker portal or send notifications during seeding.
Normal actions in the organizer UI may still queue messages; keep local mail
sending disabled, as in the standard local setup.

## Reruns and failures

An existing application with the same mock event and email is skipped. Its answers,
status, dates, and notes are preserved. Lowering `--count` never deletes applicants.
The form is not overwritten once published; incompatible edits make a run fail.

Completed rows survive a failed run. A handled failure removes only the newly
created application that failed, so rerunning can retry it. A process killed midway
may leave that one row partially prepared; reruns preserve it like any other
existing application. There is intentionally no destructive reset command.

## Local-only safeguards

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
migrations, and is never invoked by startup or deployment. It changes no schema.
It calls application stores directly, bypassing endpoint email orchestration.

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
