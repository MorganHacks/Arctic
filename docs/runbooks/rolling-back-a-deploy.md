# Rolling back a bad deploy

**Takes about 7 minutes.** That is measured, not estimated — a rollback of
staging from `f900572` to `03c9f55` took 7m10s end to end, including migrations.

## Do this

Actions → **Deploy to Azure** → Run workflow:

| Field | Value |
|---|---|
| Environment | `staging` or `production` |
| Tag | the last known good short SHA |

That is the whole procedure. Images are tagged by commit and never `:latest`,
so this re-deploys bytes that already exist rather than rebuilding and hoping
they come out the same.

Finding the last good tag:

```bash
az acr repository show-tags -n crmharctic --repository harbor --orderby time_desc -o table
```

They are commit SHAs, so `git log --oneline` tells you what each one is.

## The thing to check first

**A rollback re-runs migrations.**

Going backwards through code is safe. Going backwards through a schema is not,
and nothing here does that — the migration runner only rolls forward. So:

- Rolling back **code** onto a **newer schema**: fine, and the normal case. The
  old code ignores columns it does not know about.
- Rolling back after a migration **dropped or renamed** something the old code
  needs: the old code breaks, and rolling back further will not fix it.

Before rolling back, look at what migrations shipped between the two tags:

```bash
git diff --name-only <good-tag>..<bad-tag> -- src/atlas/MorganHacks.Migrations/Scripts/
```

If that is empty, roll back without thinking about it. If it lists something
that drops a column, a table, or a constraint, **stop** — you need a forward fix
instead, and rolling back will turn one broken thing into two.

This is why a migration that removes something is worth a second look at review
time. Adding is always reversible; removing is not.

## If the deploy itself failed

Then nothing was deployed and there is nothing to roll back. `deploy.sh` stops
before touching any service if migrations fail, precisely so a half-applied
state cannot happen. Read why:

```bash
az containerapp job logs show -g rg-mh-staging -n caj-migrations-staging \
  --container migrations --execution "$(az containerapp job execution list \
    -g rg-mh-staging -n caj-migrations-staging \
    --query 'sort_by([], &properties.startTime)[-1].name' -o tsv)"
```

## Check it worked

```bash
az containerapp show -g rg-mh-staging -n ca-harbor-staging \
  --query "properties.template.containers[0].image" -o tsv

curl -s https://<harbor-host>/api/health
```

The image tag should be the one you asked for and health should answer `ok`.

## What this does not cover

Rolling back the frontends. Those are Vercel deployments — use Vercel's own
"promote to production" on an earlier deployment, which is instant and does not
touch the database at all.

---

**Escalate to:** whoever merged the bad change, then the tech lead. If it is
data loss rather than a bad deploy, stop and read
[getting at the database](database-access.md) first — restoring is not
something to improvise.
