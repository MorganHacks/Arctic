# MorganHacks.Migrations

**The only owner of the database schema.** Runs as a pre-deploy job.

Nothing else migrates — not `lark`, not anything. Multiple services racing to migrate one database is the most common way setups like this break in production.
