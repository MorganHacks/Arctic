-- The schemas every other script depends on.
--
-- Numbered 0000 so it runs before anything that puts a table in one. DbUp
-- orders by script name, so this is not a convention to remember — it is the
-- first thing that can possibly run.
--
-- Owned by the migration runner rather than by a manual step, because the
-- alternative was a psql command somebody had to run once against each new
-- database, from a machine that could reach it. That meant opening the
-- database firewall to a laptop, and it meant a deploy that worked or failed
-- depending on who ran it.
--
-- One database, one schema per module. Separation comes from schemas, not from
-- separate servers.
--
-- This file only creates schemas. Tables are owned by the numbered scripts
-- after it — do not add DDL here, or local and deployed databases drift and
-- the drift is invisible.

CREATE SCHEMA IF NOT EXISTS identity;      -- atlas / Identity module
CREATE SCHEMA IF NOT EXISTS applications;  -- atlas / Applications module
CREATE SCHEMA IF NOT EXISTS profiles;      -- atlas / Profiles module
CREATE SCHEMA IF NOT EXISTS notify;        -- lark
