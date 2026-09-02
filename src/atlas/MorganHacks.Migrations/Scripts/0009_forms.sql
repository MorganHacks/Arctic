-- A form is a thing with a shareable link. Versions hang off it.
--
-- The previous shape tied versions straight to an event, which said "an event
-- has one form". That is not true even this year — the application is one form,
-- a mentor sign-up is another, a post-event survey a third — and it left
-- nothing stable to put in a URL, because a version changes every time somebody
-- edits a question.
--
-- The code belongs here rather than on a version for exactly that reason: a
-- link handed out on a flyer has to survive the form being edited.

CREATE TABLE applications.forms (
    id         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id   uuid        NOT NULL REFERENCES applications.events (id) ON DELETE CASCADE,

    -- What goes in the URL: forms.morganhacks.com/<code>.
    --
    -- Generated rather than chosen, from an alphabet with no 0/O/1/l, because
    -- these get read aloud at a club meeting and written on a whiteboard.
    code       text        NOT NULL UNIQUE
                           CHECK (code ~ '^[a-z2-9]{7}$'),

    name       text        NOT NULL,

    -- 'application' is the one that writes to applications.applications. The
    -- rest only ever collect answers, and keeping the distinction here means a
    -- survey can never accidentally create an applicant.
    kind       text        NOT NULL DEFAULT 'survey'
                           CHECK (kind IN ('application', 'survey')),

    -- Closed forms still resolve, so somebody following an old link is told the
    -- form has closed rather than shown a 404 they will report as broken.
    closes_at  timestamptz,
    created_by uuid        REFERENCES identity.people (id),
    created_at timestamptz NOT NULL DEFAULT now()
);

-- One application form per event. Others are unlimited.
CREATE UNIQUE INDEX forms_one_application_per_event
    ON applications.forms (event_id) WHERE kind = 'application';

CREATE INDEX forms_event_idx ON applications.forms (event_id);

-- --------------------------------------------------- versions hang off it ---
ALTER TABLE applications.form_versions
    ADD COLUMN form_id uuid REFERENCES applications.forms (id) ON DELETE CASCADE;

-- Anything already built belonged to the application form, which did not exist
-- as a row yet. Made here so no version is left orphaned.
INSERT INTO applications.forms (event_id, code, name, kind)
SELECT DISTINCT v.event_id,
       -- Deterministic from the event id, so re-running produces the same
       -- code rather than a second form. md5 rather than digest() because
       -- digest lives in pgcrypto, and every extension has to be allow-listed
       -- on Azure before it can even be created.
       --
       -- Hex has no 'l'; 0 and 1 are mapped out so the code survives being
       -- read aloud and written on a whiteboard.
       substr(translate(md5(v.event_id::text), '01', 'wx'), 1, 7),
       'Application',
       'application'
  FROM applications.form_versions v
 WHERE NOT EXISTS (
       SELECT 1 FROM applications.forms f
        WHERE f.event_id = v.event_id AND f.kind = 'application');

UPDATE applications.form_versions v
   SET form_id = f.id
  FROM applications.forms f
 WHERE f.event_id = v.event_id AND f.kind = 'application' AND v.form_id IS NULL;

ALTER TABLE applications.form_versions ALTER COLUMN form_id SET NOT NULL;

-- The one-published and one-draft rules move from the event to the form. An
-- event with three forms should have three live forms, one each.
DROP INDEX applications.form_versions_one_published_per_event;
DROP INDEX applications.form_versions_one_draft_per_event;

CREATE UNIQUE INDEX form_versions_one_published_per_form
    ON applications.form_versions (form_id) WHERE status = 'published';

CREATE UNIQUE INDEX form_versions_one_draft_per_form
    ON applications.form_versions (form_id) WHERE status = 'draft';

ALTER TABLE applications.form_versions DROP CONSTRAINT form_versions_event_id_version_key;
CREATE UNIQUE INDEX form_versions_form_version_key
    ON applications.form_versions (form_id, version);

CREATE INDEX form_versions_form_idx ON applications.form_versions (form_id, version DESC);
