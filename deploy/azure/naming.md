# Naming and tags

Azure's own abbreviations (`rg-`, `psql-`, `cae-`) rather than something
invented here. The point is that anyone who has worked on Azure before can read
this without being told, and that a resource group sorts by type instead of
being an alphabetical pile.

| Resource | Pattern | Example |
|---|---|---|
| Resource group | `rg-morganhacks-<env>` | `rg-morganhacks-staging` |
| Container registry | `crmorganhacks` | shared; registries allow no hyphens |
| Log Analytics | `log-morganhacks-<env>` | `log-morganhacks-staging` |
| Container Apps env | `cae-morganhacks-<env>` | `cae-morganhacks-staging` |
| Postgres | `psql-morganhacks-<env>` | `psql-morganhacks-staging` |
| Container app | `ca-<service>-<env>` | `ca-harbor-staging` |
| Container Apps job | `caj-<name>-<env>` | `caj-migrations-staging` |

The environment is in every name on purpose. The worst version of this mistake
is running a command against production while believing it is staging, and a
name that says which one it is makes that harder.

## Tags

Every resource carries the same four, plus `service` where it means something.

| Tag | Why |
|---|---|
| `workload` | `morganhacks`, so this is separable from anything else in the subscription |
| `environment` | `staging` or `prod` — the filter you actually want in Cost Analysis |
| `managedBy` | `bicep`, so it is clear a portal edit will be reverted on the next deploy |
| `repository` | `MorganHacks/Arctic`, so somebody finding a stray resource can find its source |
| `service` | `atlas`, `harbor`, `lark` — on the apps only |

Tags are not decoration. Cost Analysis groups by them, and next year's team
inherits a subscription where "what is this and can I delete it" has an answer.
