# Arctic

The application and review platform for **MorganHacks**, Morgan State
University's hackathon. Applicants apply through it, organizers review and
decide through it, and every email the event sends — sign-in links,
decisions, broadcasts — goes out through it.

## The six services

One Postgres database underneath, six things deployed around it: three on
Azure Container Apps, three on Vercel.

| | | |
|---|---|---|
| [`atlas`](src/atlas) | .NET, Container Apps | The API. Applications, forms, people, templates, campaigns, audit. |
| [`harbor`](src/harbor) | .NET, Container Apps | The gateway, and the only backend exposed to the internet. Strips the `/api` prefix and forwards to atlas over the internal network. |
| [`lark`](src/lark) | .NET, Container Apps | The mail worker. No ingress — it polls a Postgres queue (`notify.messages`) with `FOR UPDATE SKIP LOCKED` and sends through AWS SES. |
| [`portaladmin`](src/portaladmin) | Next.js, Vercel | The organizer console, at admin.morganhacks.com. |
| [`portalweb`](src/portalweb) | Next.js, Vercel | The hacker portal. |
| [`portalforms`](src/portalforms) | Next.js, Vercel | Public forms, at forms.morganhacks.com. |

Alongside them, [`MorganHacks.Migrations`](src/atlas/MorganHacks.Migrations)
is the schema's only owner and runs as a Container Apps job before the other
five update — never the other way around.

## Why it is shaped this way

**harbor is the only thing on the internet.** atlas has internal ingress
only; the Container Apps network is the only way to reach it. harbor decides
who a request claims to be and strips anything a caller tries to assert for
themselves; atlas is what actually checks whether that identity may do what
it is asking.

**Each Next.js app proxies `/api/*` to harbor by rewrite, not by calling it
cross-origin.** That is deliberate: the session cookie is `SameSite=Lax`, and
a cross-site fetch from the browser would never carry it. Calling the API
means calling your own origin and letting the rewrite forward it.

Database is PostgreSQL 18. Front ends deploy to Vercel; backends to Azure
Container Apps; mail goes out through AWS SES.

## Layout

```
src/     the six services
libs/    shared code — see libs/README.md for what's real and what's not
deploy/  infrastructure: Bicep for Azure, scripts for local
docs/    setup, architecture, runbooks, backlog, in-progress plans
```

## Where to go next

- **[Getting set up](docs/set-up.md)** — clone to a running stack
- **[Deploying](deploy/azure/README.md)** — what ships, and why it's Bicep and not a script
- **[Runbooks](docs/runbooks/README.md)** — what to do when something breaks
- **[docs/](docs/README.md)** — everything else: architecture, backlog, open plans

## Simulate organizer reviews

For local mock applicants, start the local stack and run
`deploy/local/seed-hackers.sh --apply`. For shared team testing, run the manual
**Seed staging applicants** GitHub Action from `main`, with **Create applicants**
checked, then select **MOCK — Organizer simulation** in the
[staging organizer console](https://admin-stg.morganhacks.com).

Both create a separate mock event and preserve review changes on reruns. Merging
alone does not seed staging; production is blocked and mock email addresses are
suppressed. See [setup instructions](docs/set-up.md#mock-applicants-for-organizer-simulations)
and [the seed guide](src/atlas/MorganHacks.Seed/README.md).
