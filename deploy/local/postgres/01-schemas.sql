-- One database, one schema per module. Separation comes from schemas, not
-- from separate servers.
--
-- Kept in step with 0000_schemas.sql in the migration runner, which is what
-- creates these anywhere the compose file does not run. Both exist because
-- compose seeds a fresh container before anything connects to it, and that is
-- one less thing to explain in the setup docs.
--
-- This file only creates the schemas. Tables are owned by
-- MorganHacks.Migrations and by nothing else — do not add DDL here, or local
-- and deployed databases will drift and the drift will be invisible.

CREATE SCHEMA IF NOT EXISTS identity;      -- atlas / Identity module
CREATE SCHEMA IF NOT EXISTS applications;  -- atlas / Applications module
CREATE SCHEMA IF NOT EXISTS profiles;      -- atlas / Profiles module
CREATE SCHEMA IF NOT EXISTS notify;        -- lark

-- Postgres gives us the job queue for free via FOR UPDATE SKIP LOCKED, which
-- is why there is no Redis or Service Bus in this stack.
