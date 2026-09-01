# Getting at the database

The database is not reachable from the internet. That is deliberate, and it is
why there is a procedure rather than a connection string somebody keeps in a
note.

**There is no jump box and there should not be one.** A long-lived machine
holding production credentials is the highest-value target in the system, needs
patching forever by a team that changes every year, and buys nothing that is not
available below.

## Use Azure Cloud Shell

Open [shell.azure.com](https://shell.azure.com) and sign in with the MorganHacks
account.

Cloud Shell runs inside Azure, so the `allow-azure-services` firewall rule
already permits it. Nothing to open, nothing to close afterwards, and no
credential ever reaches a laptop.

```bash
# staging
psql "host=psql-mh-staging.postgres.database.azure.com port=5432 \
      dbname=morganhacks user=arctic sslmode=require"
```

It asks for the password. That lives in the `DB_PASSWORD` secret on the GitHub
environment, and — for staging — in `~/.mh-staging-db-password` on the tech
lead's machine.

## Read first, write never (by hand)

Anything that changes data should be a migration or a Container Apps job, not
something typed into a prompt at 2am.

The reason is not tidiness. The audit trail is enforced by triggers, so a
hand-written `UPDATE` to an application **does** record itself — with a null
actor, which is honest and permanently unattributable. That is fine for an
emergency and bad as a habit.

If you are about to type `UPDATE` or `DELETE`, wrap it:

```sql
BEGIN;
UPDATE ... ;
-- read it back, confirm the count is what you expected
COMMIT;   -- or ROLLBACK;
```

## Questions worth having ready

```sql
-- Is mail moving, or is the queue backing up?
SELECT status, count(*) FROM notify.messages GROUP BY status ORDER BY 2 DESC;

-- Anything stuck mid-send? The sweeper should be clearing these.
SELECT count(*) FROM notify.messages
 WHERE status = 'sending' AND locked_until < now();

-- Who is suppressed, and why?
SELECT reason, count(*) FROM notify.suppressions GROUP BY reason;

-- Applications by status.
SELECT status, count(*) FROM applications.applications GROUP BY status;

-- One person's whole story, by correlation id from a support request.
SELECT status, created_at FROM notify.messages WHERE correlation_id = '...';
```

## If you genuinely need it from a laptop

Only when Cloud Shell will not do. Add your address, do the thing, **remove the
rule**:

```bash
MYIP=$(curl -s https://api.ipify.org)
az postgres flexible-server firewall-rule create -g rg-mh-staging \
  -n psql-mh-staging --rule-name temp-$USER \
  --start-ip-address "$MYIP" --end-ip-address "$MYIP"

# ... do the thing ...

az postgres flexible-server firewall-rule delete -g rg-mh-staging \
  -n psql-mh-staging --rule-name temp-$USER --yes
```

Name the rule after yourself so a rule left open has somebody's name on it.
Check for strays:

```bash
az postgres flexible-server firewall-rule list -g rg-mh-staging \
  -n psql-mh-staging -o table
```

`allow-azure-services` is the only rule that belongs there.

## Backups

Point-in-time restore, 14 days, on by default. Restoring creates a **new
server** — it does not overwrite the live one, which is what makes it safe to
try.

```bash
az postgres flexible-server restore -g rg-mh-staging \
  --name psql-mh-restore-test \
  --source-server psql-mh-staging \
  --restore-time "2026-09-01T12:00:00Z"
```

**An untested backup is a belief, not a backup.** Run this once against staging
before the event, confirm the data is there, and delete the restored server.
That is an M9 rehearsal item and it is the one people skip.

---

**Escalate to:** the tech lead. For anything that looks like data loss, stop and
get a second person on a call before typing — a restore is recoverable, a
confident `DELETE` on top of one is not.
