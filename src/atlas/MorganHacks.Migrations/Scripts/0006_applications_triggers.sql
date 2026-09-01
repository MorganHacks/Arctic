-- Invariants that hold no matter who writes.
--
-- Everything here was already correct as long as every write went through
-- PostgresApplicationStore. The point of moving it down is that "as long as
-- every write goes through" is exactly the assumption that fails: a migration,
-- a support script, or somebody fixing one row in psql at 2am during the event
-- is not going to call a C# method.
--
-- What deliberately does NOT move down: which transitions are legal, and what
-- a person is permitted to do. Those are decisions that change when the team's
-- thinking changes, and they belong in the language with the tests and the
-- tooling. This file only holds rules we would be alarmed to find violated
-- however they got violated.

-- ------------------------------------------------------------ updated_at ---
CREATE FUNCTION applications.touch_updated_at() RETURNS trigger AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER applications_touch_updated_at
    BEFORE INSERT OR UPDATE ON applications.applications
    FOR EACH ROW EXECUTE FUNCTION applications.touch_updated_at();

-- ---------------------------------------------------- lifecycle stamps ---
-- The timestamps move with the status rather than being set alongside it.
--
-- A decided_at that disagrees with the status is worse than not having the
-- column, and every one of these is something a caller would eventually forget
-- — including a caller writing raw SQL to fix something during the event.
CREATE FUNCTION applications.stamp_status_timestamps() RETURNS trigger AS $$
DECLARE
    actor uuid := NULLIF(current_setting('app.actor_id', true), '')::uuid;
BEGIN
    IF NEW.status IS NOT DISTINCT FROM OLD.status THEN
        RETURN NEW;
    END IF;

    IF NEW.status = 'submitted' THEN
        NEW.submitted_at := now();
    ELSIF NEW.status IN ('accepted', 'rejected', 'waitlisted') THEN
        NEW.decided_at := now();
        NEW.decided_by := actor;
    ELSIF NEW.status = 'confirmed' THEN
        NEW.confirmed_at := now();
    ELSIF NEW.status = 'declined' THEN
        NEW.declined_at := now();
    ELSIF NEW.status = 'checked_in' THEN
        NEW.checked_in_at := now();
        NEW.checked_in_by := actor;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER applications_stamp_status_timestamps
    BEFORE UPDATE OF status ON applications.applications
    FOR EACH ROW EXECUTE FUNCTION applications.stamp_status_timestamps();

-- -------------------------------------------------------- status history ---
-- The audit trail, made impossible to skip.
--
-- This is the one that matters. Before it, `UPDATE applications SET status =
-- 'accepted'` succeeded silently and wrote no history row, which does not
-- leave a gap in the trail — it leaves a trail that is wrong, and wrong in a
-- way nobody can detect afterwards. There is no version of "remember to write
-- the history row" that survives an incident at 2am.
--
-- Actor, reason and batch come from transaction-local settings the application
-- sets alongside the write. When nobody sets them the row is still written
-- with nulls, which is the honest record: a hand-fixed row genuinely has no
-- actor, and seeing that is how you know it was done by hand.
CREATE FUNCTION applications.record_status_change() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'UPDATE' AND NEW.status IS NOT DISTINCT FROM OLD.status THEN
        RETURN NULL;
    END IF;

    INSERT INTO applications.status_history
        (application_id, from_status, to_status, actor_id, reason, batch_id)
    VALUES (
        NEW.id,
        CASE WHEN TG_OP = 'UPDATE' THEN OLD.status END,
        NEW.status,
        NULLIF(current_setting('app.actor_id', true), '')::uuid,
        NULLIF(current_setting('app.reason', true), ''),
        NULLIF(current_setting('app.batch_id', true), '')::uuid
    );

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER applications_record_status_change
    AFTER INSERT OR UPDATE OF status ON applications.applications
    FOR EACH ROW EXECUTE FUNCTION applications.record_status_change();

-- ------------------------------------------------------------ MLH export ---
-- The consent filter, made impossible to forget.
--
-- Affiliation obliges us to send MLH registration data, but only for people
-- who ticked the data-sharing box. Sending somebody who did not is not a bug
-- you fix afterwards — the data has already left.
--
-- As a view rather than a documented WHERE clause, because a filter somebody
-- has to remember is a filter that eventually gets run without it. Selecting
-- the wrong set is not a mistake that can be made here: the rows are not
-- reachable.
--
-- The consent filter is belt and braces today, and deliberately so. MLH makes
-- that checkbox required, and the completeness constraint on the table already
-- stops an application being submitted without it — so a non-consenting
-- registrant cannot currently exist. This keeps the export correct anyway, for
-- the day somebody decides the checkbox is optional after all.
--
-- Withdrawn and incomplete applications are excluded too. Someone who never
-- finished, or asked to be removed, is not a registrant.
CREATE VIEW applications.mlh_export AS
SELECT a.id,
       a.event_id,
       a.email,
       a.phone,
       a.first_name,
       a.last_name,
       a.school,
       a.level_of_study,
       a.country,
       a.age,
       a.mlh_data_sharing_at,
       a.status
  FROM applications.applications a
 WHERE a.mlh_data_sharing_at IS NOT NULL
   AND a.status NOT IN ('incomplete', 'withdrawn');
