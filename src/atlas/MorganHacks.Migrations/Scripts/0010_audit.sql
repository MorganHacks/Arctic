-- The permission audit trail.
--
-- The RBAC doc requires that every change to somebody's access be
-- attributable: "who gave this person export at 2am" must have an answer that
-- outlives the person who could remember. Until now the answer was
-- `grants.granted_by` plus a log line, which is a record of the last decision
-- rather than a trail — it cannot say what the grant was before, it says
-- nothing at all about team membership, and a log line is gone when the
-- retention window closes.
--
-- Written by triggers, for the same reason 0006 moved status history down.
-- The alternative is an INSERT in every C# path that changes access, and
-- "as long as every write goes through PostgresIdentityStore" is exactly the
-- assumption that fails: the migration runner seeds the first super admin with
-- raw SQL a few lines below this file's own execution, a support script does
-- not call a C# method, and neither does somebody fixing one grant in psql
-- during the event. A trail with holes in it is worse than no trail, because
-- the holes are invisible and everything else looks complete.
--
-- The trigger's INSERT runs inside the caller's transaction, so the audit row
-- and the access change commit together or neither does. That is the property
-- that makes this a library and not a service: an HTTP call to an audit
-- service can fail while the grant it was recording succeeds.

CREATE SCHEMA IF NOT EXISTS audit;

-- Not in 0000_schemas.sql, which has already run everywhere. DbUp never
-- re-runs a script, so a schema added there now would exist on a laptop
-- created tomorrow and be missing from staging — the invisible drift that file
-- warns about. New schemas belong in the migration that first needs them.

-- --------------------------------------------------------------- entries ---
-- One row per change to what somebody may do. Append-only: see the guard at
-- the bottom of this file, which is where anybody looking for an UPDATE or a
-- DELETE will end up.
CREATE TABLE audit.entries (
    -- bigint rather than uuid, unlike every other table here.
    --
    -- This is a log, and a log has to be orderable. now() is transaction start
    -- time, so several entries written by one admin action share an
    -- occurred_at to the microsecond; a uuid tiebreak would order them
    -- differently on every read. A sequence orders them the way they happened.
    id           bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- Transaction start time, so every entry from one action agrees with the
    -- change it records rather than with the clock a few statements later.
    occurred_at  timestamptz NOT NULL DEFAULT now(),

    -- What happened, in the past tense, as `noun.verb`. Text rather than an
    -- enum: an enum needs a migration to add a value, and this table has to be
    -- able to record something the day it starts happening.
    action       text        NOT NULL,

    -- Who did it. NULL is a real answer and the honest one — the super-admin
    -- seed, an import, a hand-run fix have no person behind them, and putting
    -- a name against a decision nobody made is worse than admitting there
    -- isn't one. Set from `app.actor_id` on the transaction; see AuditContext.
    actor_id     uuid        REFERENCES identity.people (id),

    -- Who it was done to. Exactly one of these two is set: a person for
    -- everything that changes one person's access, a team for a change to a
    -- baseline, which changes what everybody on it may do at once.
    subject_id   uuid        REFERENCES identity.people (id),
    -- The slug as text, not a foreign key. A team that is deleted later must
    -- not take the record of what it once granted with it.
    subject_team text,

    -- The thing that changed: a team slug for a membership, a permission
    -- string for a grant or a baseline. NULL where the action is about the
    -- person themselves.
    target       text,

    -- When the access this row describes lapses. Present because it is half
    -- the answer to "why can they still do this" — a membership added with an
    -- expiry and one added without are different decisions.
    expires_at   timestamptz,

    -- Whatever else the action needs, rather than a column per action. The
    -- previous expiry on a retimed membership lives here; so does the
    -- granted_by the grants table records, which can differ from the actor
    -- when a row is written by hand.
    detail       jsonb       NOT NULL DEFAULT '{}'::jsonb,

    CONSTRAINT entries_have_one_subject
        CHECK (num_nonnulls(subject_id, subject_team) = 1)
);

-- No PII, ever. Every column above holds an id, a slug or a permission string;
-- none of them holds an address, a name or a phone number. The trail says who
-- did what to whom by id, so a copy of it that leaks is not a copy of the
-- people. Resist the first reviewer who asks for the email "so the screen does
-- not need a join" — the screen can join.

-- The two questions this table exists to answer: "what happened to this
-- person" and "what has this person been doing". Both are read newest-first.
CREATE INDEX entries_subject_idx
    ON audit.entries (subject_id, occurred_at DESC) WHERE subject_id IS NOT NULL;
CREATE INDEX entries_actor_idx
    ON audit.entries (actor_id, occurred_at DESC) WHERE actor_id IS NOT NULL;
CREATE INDEX entries_recent_idx
    ON audit.entries (occurred_at DESC);

-- ----------------------------------------------------------------- write ---
-- One place that writes the table, so the column list exists once.
--
-- SECURITY INVOKER (the default) on purpose: this runs as whoever made the
-- change, and adding privileges it does not need would make the audit trigger
-- the most powerful thing in the schema.
-- Parameters are prefixed so that none of them shares a name with a column of
-- the table being written. An unprefixed `action` here resolves differently
-- depending on where it appears, and the failure mode is a trail that records
-- the wrong thing rather than an error.
CREATE FUNCTION audit.record(
    p_action       text,
    p_subject_id   uuid,
    p_subject_team text,
    p_target       text,
    p_expires_at   timestamptz,
    p_detail       jsonb DEFAULT '{}'::jsonb
) RETURNS void AS $$
    INSERT INTO audit.entries
        (action, actor_id, subject_id, subject_team, target, expires_at, detail)
    VALUES (
        p_action,
        -- The `true` is `missing_ok`: a transaction that never set this — a
        -- migration, psql, the seed — gets NULL rather than an error, which is
        -- the point. An audit trigger that could abort the transaction it
        -- observes would make recording a change able to prevent it.
        NULLIF(current_setting('app.actor_id', true), '')::uuid,
        p_subject_id,
        p_subject_team,
        p_target,
        p_expires_at,
        p_detail
    );
$$ LANGUAGE sql;

-- -------------------------------------------------------------- allowlist ---
-- Being an organizer row IS being on the allowlist, so the INSERT is the
-- access change and there is nothing else to hook.
--
-- Hacker rows are deliberately not recorded. Registering for the event is not
-- a grant of anything, and mixing several thousand of those into this table
-- would bury the twenty rows a year that actually matter.
CREATE FUNCTION audit.record_person_change() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF NEW.kind = 'organizer' THEN
            PERFORM audit.record('organizer.added', NEW.id, NULL, NULL, NULL);
        END IF;
        RETURN NULL;
    END IF;

    -- Revocation, in both directions. Nothing in the application un-revokes
    -- anybody today; if somebody does it in psql to undo a mistake, that is
    -- precisely the change that needs to be visible afterwards.
    IF OLD.revoked_at IS NULL AND NEW.revoked_at IS NOT NULL THEN
        PERFORM audit.record('person.revoked', NEW.id, NULL, NULL, NULL);
    ELSIF OLD.revoked_at IS NOT NULL AND NEW.revoked_at IS NULL THEN
        PERFORM audit.record('person.restored', NEW.id, NULL, NULL, NULL);
    END IF;

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER people_record_access_change
    AFTER INSERT OR UPDATE OF revoked_at ON identity.people
    FOR EACH ROW EXECUTE FUNCTION audit.record_person_change();

-- ------------------------------------------------------------ membership ---
-- Team membership is where most access comes from, so it is where most of the
-- trail comes from. The slug is looked up rather than stored as the team id,
-- because "logistics" is the answer somebody wants six months later and the
-- uuid is not.
CREATE FUNCTION audit.record_membership_change() RETURNS trigger AS $$
DECLARE
    team uuid := CASE TG_OP WHEN 'DELETE' THEN OLD.team_id ELSE NEW.team_id END;
    slug text;
BEGIN
    -- Falls back to the id when the team row is already gone: a membership
    -- deleted by cascade from a deleted team still happened, and an entry
    -- naming a uuid beats no entry at all.
    SELECT t.slug INTO slug FROM identity.teams t WHERE t.id = team;
    slug := coalesce(slug, team::text);

    IF TG_OP = 'INSERT' THEN
        PERFORM audit.record(
            'team.joined', NEW.person_id, NULL, slug, NEW.expires_at);
    ELSIF TG_OP = 'DELETE' THEN
        PERFORM audit.record('team.left', OLD.person_id, NULL, slug, NULL);
    ELSIF NEW.expires_at IS DISTINCT FROM OLD.expires_at THEN
        -- Retiming is the upsert path: "on the judge team until Sunday" and
        -- "actually, Monday" arrive as an INSERT then an UPDATE. Recording the
        -- expiry it had before is what makes shortening distinguishable from
        -- extending afterwards.
        PERFORM audit.record(
            'team.retimed', NEW.person_id, NULL, slug, NEW.expires_at,
            jsonb_build_object('previousExpiresAt', OLD.expires_at));
    END IF;

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER team_members_record_change
    AFTER INSERT OR UPDATE OR DELETE ON identity.team_members
    FOR EACH ROW EXECUTE FUNCTION audit.record_membership_change();

-- ---------------------------------------------------------------- grants ---
-- The individually granted permissions. Fewer rows than membership and more
-- of them worth reading: a direct grant is somebody deciding one person needs
-- something their team does not confer.
CREATE FUNCTION audit.record_grant_change() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        PERFORM audit.record(
            'grant.removed', OLD.person_id, NULL, OLD.permission, NULL);
        RETURN NULL;
    END IF;

    IF TG_OP = 'UPDATE'
       AND NEW.expires_at IS NOT DISTINCT FROM OLD.expires_at
       AND NEW.granted_by IS NOT DISTINCT FROM OLD.granted_by THEN
        RETURN NULL;
    END IF;

    -- granted_by rides along in detail rather than being assumed equal to the
    -- actor. They are the same on every path through the API and can differ on
    -- a row written by hand, and the trail should show that rather than hide
    -- it.
    PERFORM audit.record(
        CASE TG_OP WHEN 'INSERT' THEN 'grant.added' ELSE 'grant.changed' END,
        NEW.person_id, NULL, NEW.permission, NEW.expires_at,
        jsonb_build_object('grantedBy', NEW.granted_by));

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER grants_record_change
    AFTER INSERT OR UPDATE OR DELETE ON identity.grants
    FOR EACH ROW EXECUTE FUNCTION audit.record_grant_change();

-- --------------------------------------------------------- team baselines ---
-- Changing what a team confers changes what everybody on it may do, without
-- touching a single person's row. The RBAC doc makes this an UPDATE rather
-- than a code change on purpose, which means it is a permission change with no
-- deploy, no review and — until this trigger — no record.
--
-- The subject is the team, because that is who it was done to. A screen
-- answering "why can this person export" has to look at both: their own rows,
-- and the baselines of the teams they are on.
CREATE FUNCTION audit.record_baseline_change() RETURNS trigger AS $$
DECLARE
    team       uuid := CASE TG_OP WHEN 'DELETE' THEN OLD.team_id ELSE NEW.team_id END;
    permission text := CASE TG_OP WHEN 'DELETE' THEN OLD.permission ELSE NEW.permission END;
    slug       text;
BEGIN
    SELECT t.slug INTO slug FROM identity.teams t WHERE t.id = team;
    slug := coalesce(slug, team::text);

    PERFORM audit.record(
        CASE TG_OP WHEN 'DELETE' THEN 'baseline.removed' ELSE 'baseline.added' END,
        NULL, slug, permission, NULL);

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER team_permissions_record_change
    AFTER INSERT OR DELETE ON identity.team_permissions
    FOR EACH ROW EXECUTE FUNCTION audit.record_baseline_change();

-- ----------------------------------------------------------- append-only ---
-- There is no update path and no delete path. This is where somebody looks
-- for one, so this is where it says so.
--
-- Not a convention, not a code review rule, not "the store has no method for
-- it" — a trail that can be edited by whoever is being audited is not
-- evidence, and the person most motivated to edit it has a database
-- connection. Refusing in the database is the only version of this that holds
-- against psql.
--
-- If retention law ever requires deleting entries, that is a migration which
-- drops this trigger, does the deletion and puts it back — a deliberate,
-- reviewed, recorded act. It must never be something that succeeds by
-- accident at 2am.
CREATE FUNCTION audit.refuse_rewrite() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION
        'audit.entries is append-only; % is not permitted', TG_OP
        USING HINT = 'Deleting entries needs a migration that drops '
                     'audit_entries_are_append_only deliberately.';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER audit_entries_are_append_only
    BEFORE UPDATE OR DELETE ON audit.entries
    FOR EACH ROW EXECUTE FUNCTION audit.refuse_rewrite();

-- TRUNCATE bypasses row triggers entirely, so it needs its own. Without this
-- the append-only guarantee above is one word long.
CREATE TRIGGER audit_entries_refuse_truncate
    BEFORE TRUNCATE ON audit.entries
    FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_rewrite();
