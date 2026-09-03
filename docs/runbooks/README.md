# runbooks

What to do when something breaks. **Written before the event, not during** —
nobody writes documentation at 2am on event Saturday.

One page each, each ending with an escalation name.

## Written

- [Rolling back a bad deploy](rolling-back-a-deploy.md) — measured at ~7 minutes
- [Getting at the database](database-access.md) — and why there is no jump box
- [Nobody is receiving magic links](nobody-is-receiving-magic-links.md) — the
  failure where everything looks healthy

## Still to write

- [ ] Registration form returning errors — the form exists now, so this one is
      only waiting on somebody writing it
- [ ] Broadcast circuit breaker tripped — waits on the breaker existing (M7)
- [ ] Check-in scanners offline at the venue — phase two
- [ ] Database connection exhaustion
- [ ] MLH code-of-conduct incident response — a procedure, not a feature

## What makes one of these worth having

Written from something somebody actually did, with real numbers. The rollback
page says seven minutes because a rollback was performed and timed, not because
seven felt about right. A runbook whose timings are guesses is one nobody trusts
the second time.

Every page ends with who to escalate to, because the worst moment to work out
whose problem something is, is while it is happening.
