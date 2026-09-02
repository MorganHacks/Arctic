# audit

Audit logging. **A library, deliberately not a service** — a network hop would mean audit writes could fail independently of the action they record.

The trail lives in `audit.entries` and is written by triggers, not by this
code. See `src/atlas/MorganHacks.Migrations/Scripts/0009_audit.sql`.

## What this library is for

Two things, and nothing else:

- **`AuditContext`** — tells a transaction who is acting, via the
  `app.actor_id` setting the triggers read. Requires a transaction, because
  the setting is transaction-local and would otherwise be gone before the
  statement it was set for.
- **`IAuditTrail`** — reads entries back, filtered by subject or actor.

## What it deliberately does not have

**No write method.** The database records the change, inside the same
transaction as the change. A write method here would be a second path that can
be forgotten, or called without the change it claims to describe.

**No update or delete path.** `audit.entries` refuses both, in the database.
A trail that can be edited by whoever is being audited is not evidence.

## Adding it to a service

```csharp
builder.Services.AddAuditTrail();   // read side; the trail is written regardless
```

Then, in any write that changes access:

```csharp
await using var tx = await connection.BeginTransactionAsync(ct);
await AuditContext.SetActorAsync(connection, tx, actorId, ct);
// ... the change ...
await tx.CommitAsync(ct);
```

Skipping `SetActorAsync` does not skip the audit row. It records a null actor,
which is how a change made by hand looks — so a path that forgets it is
mislabelled rather than missing, and that is what the tests check.
