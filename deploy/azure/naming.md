# Naming and tags

Azure's own abbreviations (`rg-`, `psql-`, `cae-`) rather than something
invented here. The point is that anyone who has worked on Azure before can read
this without being told, and that a resource group sorts by type instead of
being an alphabetical pile.

| Resource | Pattern | Example |
|---|---|---|
| Resource group | `rg-mh-<env>` | `rg-mh-staging`, `rg-mh-prod` |
| Container registry | `crmharctic` | shared; registries allow no hyphens |
| Log Analytics | `log-mh-<env>` | `log-mh-staging`, `log-mh-prod` |
| Container Apps env | `cae-mh-<env>` | `cae-mh-staging`, `cae-mh-prod` |
| Postgres | `psql-mh-<env>` | `psql-mh-staging`, `psql-mh-prod` |
| Container app | `ca-<service>-<env>` | `ca-harbor-staging`, `ca-harbor-prod` |
| Container Apps job | `caj-<name>-<env>` | `caj-migrations-staging` |
| Storage account | `stmh<env><hash>` | resumes; globally unique, no hyphens allowed |
| Pull identity | `id-mh-<env>-pull` | `id-mh-staging-pull`, `id-mh-prod-pull` |

The environment is in every name on purpose. The worst version of this mistake
is running a command against production while believing it is staging, and a
name that says which one it is makes that harder.

## Regions are not part of the name, and they differ

`rg-mh-shared` and `rg-mh-staging` are both in **Central US**. `rg-mh-prod` is
in **East US 2** — a deliberate split, not drift, and the reasoning is worth
repeating here because a name gives no hint of it.

The registry has to stay in Central US: a resource cannot change region, and a
managed identity cannot be granted `AcrPull` across tenants, so moving it means
standing up a second registry rather than relocating this one. That is why
`main.bicep` takes a `sharedLocation` parameter, separate from the
per-environment `location` — conflating the two was the exact bug that broke
production's first deploy at the registry stage (`InvalidResourceLocation`,
see
[`docs/runbooks/first-production-deploy.md`](../../docs/runbooks/first-production-deploy.md)).

Production's own resources moved to East US 2 because every API call from the
frontends is relayed through Vercel's rewrite, and Vercel serves this project
from `iad1`, in northern Virginia. Central US is Iowa — a cross-country hop on
every single request. East US 2 is the same metro as `iad1`; `prod.bicepparam`
has the full reasoning, including that Postgres happens to be slightly cheaper
there too. Staging has no reason to move and stays where it started.

## Tags

Every resource carries the same four, plus `service` where it means something.

| Tag | Why |
|---|---|
| `workload` | `mh`, so this is separable from anything else in the subscription |
| `environment` | `staging` or `prod`; `shared` on the registry's own group, which belongs to neither — the filter you actually want in Cost Analysis |
| `managedBy` | `bicep`, so it is clear a portal edit will be reverted on the next deploy |
| `repository` | `Morgan-Hacks/Arctic`, so somebody finding a stray resource can find its source |
| `service` | `atlas`, `harbor`, `lark` — on the apps only |

Tags are not decoration. Cost Analysis groups by them, and next year's team
inherits a subscription where "what is this and can I delete it" has an answer.
