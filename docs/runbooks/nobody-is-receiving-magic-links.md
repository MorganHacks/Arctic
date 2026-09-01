# Nobody is receiving magic links

The worst failure in the system, because **everything looks healthy while it
happens.** Services are up, health checks pass, no error rate moves. People
simply cannot log in.

## The signal

`magic_link.requested` staying flat or healthy while `magic_link.consumed`
collapses. Both are emitted as an `event` property on a log line, so an
aggregator can count them.

If somebody reports it before the graph does, that is the same symptom.

## Work down this list, in order

### 1. Is anything being queued at all?

```sql
SELECT status, count(*) FROM notify.messages
 WHERE priority = 0 AND created_at > now() - interval '1 hour'
 GROUP BY status;
```

**Nothing at all** → the problem is upstream of mail. Atlas is not queueing.
Check that the `magic_link` template exists, because a missing one is logged
loudly and then drops the send:

```sql
SELECT key, from_local, from_domain FROM notify.templates WHERE key = 'magic_link';
```

**Rows in `pending`, none in `sent`** → lark is not sending. Go to 2.

**Rows in `sent`** → we handed them to SES and they are not arriving. Go to 4.

### 2. Is lark running, and does it think it can send?

```bash
az containerapp logs show -g rg-mh-staging -n ca-lark-staging --tail 30 --type console
```

Look for `No mail provider configured; not claiming anything to send.` That
means `AWS_REGION` and the credentials are missing. Lark is deliberately idle
rather than crash-looping, and **nothing is lost** — the queue goes out as soon
as they are set.

Fix: set `AWS_REGION`, `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` on the
environment and redeploy.

### 3. Is the address suppressed?

```sql
SELECT * FROM notify.suppressions WHERE email = 'them@example.com';
```

A hard bounce or complaint blocks both lanes. An unsubscribe blocks broadcast
only, so it should **not** stop a sign-in link — if it does, that is a bug worth
reporting rather than working around.

Removing a suppression is a decision, not a fix. If the address hard-bounced,
mail to it will bounce again and hammering it is how the sending domain gets
blocked.

### 4. Is SES accepting and then not delivering?

```sql
SELECT status, last_error, count(*) FROM notify.messages
 WHERE created_at > now() - interval '1 hour'
 GROUP BY status, last_error;
```

`sent` means SES accepted it. `delivered` only arrives later by webhook. If
everything is stuck at `sent` and nothing reaches `delivered`, the webhook is
not being delivered — check the SNS subscription still points at
`https://<host>/api/webhooks/ses`.

Then check SES itself in the AWS console: **still in the sandbox** only sends to
verified addresses, and a **paused sending account** silently accepts nothing.

### 5. Is the domain still verified?

If DKIM records for `auth.morganhacks.com` were changed or removed, SES stops
sending from it. Check the domain identity in SES.

## What not to do

**Do not re-queue by hand in a loop.** If a thousand messages are stuck, the
cause is one of the five things above, and sending them again without fixing it
sends a thousand more into the same wall — and a burst of retries against a
provider that is already unhappy is how a warning becomes a suspension.

---

**Escalate to:** the tech lead, then whoever owns the AWS account. If SES has
suspended the account, that is not a fix-it-yourself situation — it needs a
written response to AWS, and it needs to happen the same day.
