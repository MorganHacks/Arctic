-- Identity: people, the organizer allowlist, teams, grants, sessions and
-- magic-link tokens.
--
-- Owned by MorganHacks.Migrations and nothing else. Two services racing to
-- migrate the same database is the most common way setups like this break in
-- production, so there is exactly one owner.

-- ---------------------------------------------------------------- people ---
-- Organizers and hackers are both people, but never the same person row.
-- An organizer account is deliberately not also an applicant account: the
-- unique email index below is what enforces that. An organizer who wants to
-- test the hacker flow registers with a different address.
CREATE TABLE identity.people (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    kind         text        NOT NULL CHECK (kind IN ('hacker', 'organizer')),
    email        text        NOT NULL,
    full_name    text,

    -- Google's stable subject id, bound on first successful login.
    --
    -- Bound rather than trusted up front for two reasons: an organizer who
    -- changes their Google email does not get locked out, because we match on
    -- this rather than on the address; and nobody can claim an allowlisted
    -- address they do not actually control, because the first legitimate
    -- login is what binds it.
    google_sub   text,

    -- Presence of an organizer row IS the allowlist. Revoking access means
    -- setting this and deleting their sessions, which takes effect on the
    -- next request because sessions are opaque.
    revoked_at   timestamptz,

    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now()
);

-- Case-insensitive uniqueness at the database, not in application code.
CREATE UNIQUE INDEX people_email_lower_key ON identity.people (lower(email));
CREATE UNIQUE INDEX people_google_sub_key  ON identity.people (google_sub)
    WHERE google_sub IS NOT NULL;

-- ----------------------------------------------------------------- teams ---
-- A team grants a baseline permission set. Adding someone to registration
-- gives them the whole registration baseline at once, so nobody hand-picks
-- checkboxes for twenty people and nobody ends up under-provisioned.
CREATE TABLE identity.teams (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    slug        text        NOT NULL UNIQUE,
    name        text        NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- Permission strings are validated in code against the Permission enum, which
-- is the source of truth. A check constraint here would mean a migration
-- every time a permission is added.
CREATE TABLE identity.team_permissions (
    team_id     uuid NOT NULL REFERENCES identity.teams (id) ON DELETE CASCADE,
    permission  text NOT NULL,
    PRIMARY KEY (team_id, permission)
);

CREATE TABLE identity.team_members (
    person_id   uuid        NOT NULL REFERENCES identity.people (id) ON DELETE CASCADE,
    team_id     uuid        NOT NULL REFERENCES identity.teams (id) ON DELETE CASCADE,
    -- Useful for the judge team specifically, whose access should die the day
    -- after the event rather than when somebody remembers.
    expires_at  timestamptz,
    created_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (person_id, team_id)
);

-- ---------------------------------------------------------------- grants ---
-- Individual grants layer on top of team baselines. Effective permissions are
-- the union of the two.
--
-- Additive only. There is deliberately no "team grants it but this person is
-- denied": subtractive overrides make effective permissions impossible to
-- reason about. If someone should not have a team's permission, they should
-- not be on that team.
CREATE TABLE identity.grants (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    person_id   uuid        NOT NULL REFERENCES identity.people (id) ON DELETE CASCADE,
    permission  text        NOT NULL,
    -- Expiry means cleanup happens on its own. A reviewer pulled in to clear
    -- a backlog should not still hold PII export in March.
    expires_at  timestamptz,
    granted_by  uuid        REFERENCES identity.people (id),
    granted_at  timestamptz NOT NULL DEFAULT now(),
    UNIQUE (person_id, permission)
);

CREATE INDEX grants_person_idx ON identity.grants (person_id);

-- -------------------------------------------------------------- sessions ---
-- A session token is a random reference to this row, never a JWT.
--
-- A JWT is valid until it expires and cannot be taken back: revoke someone at
-- 2pm against a token good until 3pm and they keep an hour of access to
-- applicant PII. Revoking an opaque session is a database write that takes
-- effect on the very next request. The cost is one lookup per request, which
-- at this volume is nothing.
--
-- Only the hash is stored, for the same reason as passwords: a database leak
-- should not hand out live sessions.
CREATE TABLE identity.sessions (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    person_id     uuid        NOT NULL REFERENCES identity.people (id) ON DELETE CASCADE,
    token_hash    bytea       NOT NULL UNIQUE,
    created_at    timestamptz NOT NULL DEFAULT now(),
    expires_at    timestamptz NOT NULL,
    revoked_at    timestamptz,
    last_seen_at  timestamptz,
    user_agent    text,
    -- inet rather than text so a range query is possible during an incident.
    ip            inet
);

CREATE INDEX sessions_person_idx ON identity.sessions (person_id);
CREATE INDEX sessions_expiry_idx ON identity.sessions (expires_at)
    WHERE revoked_at IS NULL;

-- --------------------------------------------------- magic link tokens ---
-- No password is ever created, stored or reset.
--
-- Only the hash is stored, so a leak cannot hand out live login links.
-- Fifteen minutes and single use, consumed on click rather than on expiry,
-- because email sits in inboxes and gets forwarded.
CREATE TABLE identity.magic_link_tokens (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    person_id    uuid        NOT NULL REFERENCES identity.people (id) ON DELETE CASCADE,
    token_hash   bytea       NOT NULL UNIQUE,
    created_at   timestamptz NOT NULL DEFAULT now(),
    expires_at   timestamptz NOT NULL,
    consumed_at  timestamptz,
    requested_ip inet
);

CREATE INDEX magic_link_person_idx ON identity.magic_link_tokens (person_id);

-- Sweeping expired tokens is cheap and keeps the table from growing without
-- bound; the partial index keeps the sweep off live rows.
CREATE INDEX magic_link_expiry_idx ON identity.magic_link_tokens (expires_at)
    WHERE consumed_at IS NULL;
