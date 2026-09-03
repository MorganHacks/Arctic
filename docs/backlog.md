# Backlog

Known gaps, written down so they stop living in people's heads and being
rediscovered one at a time.

Everything here was checked against the code on **2026-09-03**. Where a claim
could be tested rather than read, it was, and the test is written next to it so
the next person can re-run it instead of trusting this page.

Ordered by urgency **relative to registration opening**, because that is the
only deadline most of this has. Severity is what it is: nothing here is
inflated to get attention and nothing is softened because it is awkward.

This is not a roadmap. It is the list of things that are already true and
already wrong.

---

## Before registration opens

### The public form is not deployed, and the hacker portal cannot reach the API

`forms.morganhacks.com` and `forms-stg.morganhacks.com` are attached to
`morganhacks-portalforms`, verified, and answer Vercel's
`DEPLOYMENT_NOT_FOUND`. There is no production deployment for them to point at.

Every deployment of that project is cancelled by its Ignored Build Step, and
Vercel says why:

```
Running "test ! -d src/portalforms && exit 0 || exit 1"
The deployment was canceled because the Ignored Build Step command returned exit code 0.
```

The command runs with the project's root directory as the working directory,
which is `src/portalforms`. From in there, `src/portalforms` does not exist, so
`test ! -d` succeeds, `exit 0` runs, and exit 0 means *skip the build*. It reads
like it was written to mean the opposite.

`vercel ls morganhacks-portalforms` shows the same thing from the other side:
every deployment, preview and production, `Canceled` in under a second.

Two more settings are missing, and the first is hidden behind the build:

- **`API_ORIGIN` is not set on portalforms, in any environment.**
  `vercel api "/v10/projects/morganhacks-portalforms/env"` returns an empty
  list. The fallback is `http://localhost:5050`, so even once it builds, every
  server-side call goes nowhere.
- **`API_ORIGIN` is not set on portalweb either.** That one is live now:
  `lib/api.ts` sends every server-side call to `http://localhost:5050`, so no
  page in the hacker portal can reach atlas. `www.morganhacks.com/portal` still
  answers 200, because a failed call renders the same as not being signed in —
  which is why nothing about it looks broken from outside.

Only portaladmin has `API_ORIGIN`, scoped to production and to the `staging`
branch. That is the pattern the other two should copy.

What breaks: the form a flyer points at does not exist, and the portal an
applicant checks their status on cannot see their application. Everything
upstream works — a form can be built, published, and its share link copied — and
the link goes nowhere.

**Urgency: this is the one that has to be fixed first.** It is three settings on
two Vercel projects and no code, and until it is done nothing else about
registration can be tested end to end.

### Every per-IP rate limit can be bypassed with a header

`atlas` and `harbor` both call `options.KnownProxies.Clear()` and
`options.KnownIPNetworks.Clear()` and then add only what `Network:KnownProxies`
and `Network:KnownNetworks` name. Neither variable is set in
`deploy/azure/modules/apps.bicep`, so both lists are empty in staging and in
production.

ASP.NET's forwarded-headers middleware only performs the known-proxy check when
at least one of those lists is non-empty. With both empty there is no check, and
`X-Forwarded-For` is honoured from whoever sent it.

Both `Program.cs` files say the opposite — "with nothing configured this trusts
nothing" — and so did
[`architecture/deployments.md`](architecture/deployments.md) until this page was
written. The comment describes the intent; the middleware does not implement it.

Measured, not assumed. Against a locally running atlas with nothing configured:

```bash
curl -s -o /dev/null -H 'X-Forwarded-For: 203.0.113.7' \
  'http://localhost:5080/dev/sign-in?email=someone@morgan.edu'

docker compose exec -T postgres psql -U arctic -d morganhacks -qAt \
  -c "SELECT ip FROM identity.sessions ORDER BY created_at DESC LIMIT 1;"
# 203.0.113.7   — the request came from 127.0.0.1
```

What breaks:

- **Every per-identity limit is one header away from unlimited.** A caller who
  sends a different `X-Forwarded-For` per request gets a fresh bucket per
  request. That covers atlas's `form-submit` (60 per five minutes),
  `resume-upload` (60 per five minutes) and `magic-link` (5 per fifteen
  minutes), and harbor's `auth-strict` and `standard` policies.
- **`identity.sessions.ip` is caller-controlled**, which makes the column worse
  than empty: it looks like evidence and is not.

This is the exact failure `deployments.md` warns about under "the usual version
of this fix", reached from the other direction — not by clearing the list on
purpose, but by clearing it and then filling it with nothing.

The per-address magic-link limit in `AuthEndpoints.TooManyFor` is unaffected: it
partitions on the address, not the caller, so the one control that stops a
person being mailed repeatedly still holds.

**Urgency: fix before registration opens.** It is a handful of environment
variables plus the Container Apps subnet, and it needs deciding whether an empty
list should mean "trust nothing" — which is what everything written about it
assumes.

### There is no way to move an application through review

`IApplicationStore.TransitionAsync` is complete: it takes the row lock, refuses
an illegal transition through `StatusTransition.Validate`, sets the actor,
reason and batch id the history trigger reads, and writes. It has tests.

Nothing calls it. There is no route in `MorganHacks.Api` that changes an
application's status — `grep -rn "MapPost\|MapPatch" src/atlas/MorganHacks.Api`
lists the whole surface, and none of it is a decision. The organizer console has
a responses screen and no accept, reject, waitlist or withdraw on it.

What breaks: applications can be collected and read and exported, and cannot be
decided. There is also nothing that mails a decision, so the queue lark drains
would have nothing to drain even once the decision existed.

**Urgency: not blocking the day registration opens, blocking the day it
closes.** Worth starting before then, because the missing half is the console
screen and the endpoint, not the model underneath.

### Cloudflare rate limiting is not configured

`harbor/Program.cs` has said from the beginning that the two layers do different
jobs: Cloudflare absorbs volume, harbor handles the per-identity limits
Cloudflare cannot express. Only the second half exists. There is no Cloudflare
rate-limiting configuration anywhere in the repository, and DNS is the only
thing Cloudflare is currently doing for us.

What breaks: nothing, until somebody points volume at the public form. Then
there is no layer in front of it that sees the real connection, and harbor's own
limits are the ones bypassable by a header above.

**Urgency: before registration opens.** The public form is the one
unauthenticated write path in the platform and the only one a flyer advertises.

### Postgres accepts connections from any Azure tenant

`platform.bicep` creates the `0.0.0.0-0.0.0.0` firewall rule, which is Azure's
"allow all Azure services". Any resource in any tenant can open a connection,
with only the password in the way. The connection string also sets
`Trust Server Certificate=true`, so the TLS on that connection is encrypted but
not authenticated.

The file is honest about this and about why: a consumption Container Apps
environment has no fixed egress address, and narrowing the rule to the
environment's static IP was tried against staging and broke every connection.

The fix is VNet integration with a private endpoint, and VNet cannot be added to
an existing Container Apps environment — so it means building a new one.

**Urgency: before production holds real applicant data.** Not worth rebuilding
staging for on its own.

---

## Worth fixing before registration, not blocking it

### A survey can be built, published and shared, and stores nothing

`PublicFormEndpoints.Submit` answers **501** for any form whose kind is not
`application`:

```csharp
if (!form.IsApplication)
{
    log.LogWarning(
        "A submission arrived for a form that has nowhere to store answers. {code}",
        form.Code);
    return Results.Json(..., statusCode: StatusCodes.Status501NotImplemented);
}
```

Only applications persist answers, because `applications.applications` is the
only table an answer set has to go in. Refusing rather than accepting and
dropping is the right call — somebody who gets a 200 believes they replied — but
nothing earlier in the flow says so. The console offers "application or survey"
at creation, the builder works, publish works, the share link works, and the
public page renders every question. The failure appears the first time a person
presses submit.

`FormResponseEndpoints` is consistent with this: a survey's responses list is an
explicit empty page rather than a query that finds nothing.

What breaks: any survey put in front of people collects nothing, and the people
who filled it in are told "This form is not accepting responses yet" after
answering.

**Urgency: before any survey goes out.** It does not block the application form,
which is the thing registration needs. The trap is that nothing warns the author
until it is too late to matter.

### Abandoned resume uploads are never cleaned up

A row in `applications.resume_uploads` is written when the file arrives, and
`claimed_at` is set when a submit spends it. Closing the tab half-way through a
form is the ordinary way to leave one unspent.

`0013_resume_uploads.sql` creates the partial index a sweeper would read and
says so:

```sql
CREATE INDEX resume_uploads_unclaimed_idx
    ON applications.resume_uploads (created_at) WHERE claimed_at IS NULL;
```

Nothing reads it. There is no background service anywhere in atlas
(`grep -rni "BackgroundService\|IHostedService" src/atlas` finds only a comment
in a test), and `IResumeStore` has no delete method at all — so a sweeper needs
a new interface method as well as a job.

What breaks: a storage bill, and a pile of CVs belonging to people who never
applied and never will. The second is the reason this is not purely a cost item.

**Urgency: not blocking. Worth doing while the pile is small**, because a
sweeper written after registration has to be careful about a window it does not
have to be careful about now.

### Nothing delivers email on a developer's machine

`docker-compose.yml` runs mailpit, and `dev.sh` prints its URL. No code in the
repository speaks SMTP: `grep -rn "Smtp\|1025" --include='*.cs' src` finds
nothing, and `lark/Program.cs` registers `SesEmailProvider` when `AWS_REGION` is
set and `UnconfiguredEmailProvider` when it is not. There is no third option.

What breaks: a message queued locally sits at `pending` in `notify.messages`
forever, and a developer waiting on `localhost:8025` for a sign-in link waits
for something that was never going to arrive. `set-up.md` now says how to read
the link out of the queue instead.

**Urgency: not blocking anything deployed.** It costs every new contributor the
same confused half hour, which is the argument for fixing it.

---

## After registration, or the next time it annoys somebody

### Reading a form's draft creates one

`GET /admin/forms/{id}/draft` calls `IFormStore.DraftAsync`, which creates a
draft version if none exists, seeded from whatever is published. That is a write
on a GET, and `AdminFormEndpoints` says so in as many words.

The reasoning holds — the alternative is a builder that shows nothing until
somebody presses a button whose only honest label is "start editing" — but the
consequences are real:

- The endpoint cannot be prefetched or retried blindly. Two requests are fine
  because the create is conditional, but a link-prefetcher touching the forms
  list now writes rows.
- The draft's author becomes whoever opened the builder first, not whoever
  edited it, and the version history gains a row nobody typed into.
- It sits behind `applications.view`, which is the wider read permission, so
  somebody who cannot edit a form can still cause a draft of it to exist.

**Urgency: low.** Worth a POST when the builder is next touched.

### A section with no questions under it is a step with nothing on it

`portalforms/app/[code]/steps.ts` cuts a form into steps at each section, and
keeps a section that has no questions under it. That is deliberate and it is
documented in the file — an introduction or a page of instructions is a
legitimate thing for a form to want.

Nothing distinguishes that from a mistake. `FormValidation` refuses a form made
only of sections ("This form does not ask anything yet") and refuses a section
that is required, has options, or claims a column. It does not object to two
sections in a row, or to a section left at the end of the list, and either
produces a step an applicant lands on with nothing to do and a Next button.

What breaks: nothing, correctness-wise. Somebody's form has a blank page in it
and nobody notices until an applicant asks.

**Urgency: low.** A publish-time warning, not an error, is probably the whole
fix.

### `scripts/try-login` stops at step 7

`set-up.md` used to offer this as the way to watch sign-in work end to end. Run
it and it fails:

```
7. The link (from the log, standing in for the email lark will send)
  ✗ No link was issued — is docs-check@example.com really in the database?
```

The address is in the database. Two things are wrong, and either alone would be
enough:

- The script greps the API log for `http://localhost:3000/auth/consume?token=`.
  The link is built from `ConsumePath`, which is now `/api/auth/consume` —
  the browser reaches atlas through the front end's proxy.
- Nothing writes the link to the log at all. It goes into the message queue as a
  template variable, and `link` is on the `Redaction` denylist precisely so that
  a sign-in link never reaches a log line.

The error message is the worst part: it blames the database for something that
has nothing to do with the database.

**Urgency: low, but the fix is small.** Read the link out of
`notify.messages.rendered_body_text` — `set-up.md` documents the query — or
delete the step.

### Forcing a staging rebuild of portaladmin or portalforms fails

`deploy-staging.yml` has per-service switches for every service. Only
`portalweb` has an implementation; ticking `portaladmin` or `portalforms` runs
the `backends` job, which exits 1 saying they have "no staging deploy target
yet". Both are deployed on Vercel with `admin-stg.morganhacks.com` and
`forms-stg.morganhacks.com` bound to the `staging` branch.

The same job also errors for atlas, lark and harbor, whose own input
descriptions correctly say they are deployed by `deploy-azure.yml`.

What breaks: nothing that moving the branch does not already do — a push
rebuilds all three. Only the force path, for re-running a service when the code
has not changed, is missing.

**Urgency: low.** It is a stale message more than a missing feature.

### A change to `libs/ui` does not rebuild portalweb

`morganhacks-portalweb`'s Ignored Build Step on Vercel is:

```
git diff --quiet HEAD^ HEAD ./
```

It runs with the project's root directory as the working directory, so `./` is
`src/portalweb` and nothing else. `src/portalweb/app/portal/portal.css` imports
`../../../../libs/ui/tokens.css`, which is where every colour in the portal
comes from.

A commit that only touches `libs/ui/` therefore skips portalweb's build.
portaladmin has no ignore step and rebuilds on every push, so it is unaffected;
portalforms has the broken one above and never builds at all.

What breaks: the palette changes on the console and not on the public site, and
whoever made the change has no reason to suspect the build was skipped. It looks
like a caching problem and it is not.

`pr.yml` already learned this lesson on the CI side, where the comment on the
paths filter says it in as many words: every lib a service compiles into belongs
in its filter.

**Urgency: low, and the fix is one API call** — widen the command to cover
`libs/ui`, or drop it and let portalweb build every time like portaladmin does.
Worth doing in the same sitting as the portalforms ignore step, since it is the
same setting on a neighbouring project.

### There is no way to save a part-filled application

`applications.applications` has an `incomplete` status and the completeness
constraint that goes with it, but the status only exists inside the submit
transaction — `PostgresSubmissionStore` inserts as `incomplete` and moves to
`submitted` before it commits. Nothing writes a row that stays incomplete.

What breaks: a long form is one sitting. Someone who closes the tab starts
again, and the resume they uploaded on the way is one of the orphans above.

**Urgency: low for a first registration**, and it needs a decision before it
needs code: identifying who is coming back means either a sign-in before the
form or a link mailed to an address we have not verified yet.

---

## Fixed, recorded so it is not re-reported

**Staging deploys failing on a missing Azure role assignment.** Runs of
`deploy-azure.yml` between 2026-09-02 15:31 and 2026-09-03 01:44 failed in the
platform pass:

```
Authorization failed for template resource ... of type
'Microsoft.Authorization/roleAssignments'. The client ... does not have
permission to perform action 'Microsoft.Authorization/roleAssignments/write'
```

The deploying identity could not grant Storage Blob Data Contributor on the
resume storage account. Every run since has succeeded, staging harbor answers
`/api/health`, and the last commit to touch a backend path is the one staging is
running — so staging is not behind main.
