# lark

Email worker. Campaigns, send queue, bounce handling. Owns `notify.*`.

**No ingress.** It picks up work from Postgres and pushes results out — nothing routes to it and nothing queries it. See `doc-starter/morganhacks-notify.md`.

Milestone M4.
