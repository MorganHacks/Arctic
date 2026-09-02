-- The application form, as data.
--
-- The questions were the thing blocking this milestone: nothing could be built
-- until somebody decided what to ask. Storing the form rather than coding it
-- turns that from an engineering dependency into a decision the registration
-- team makes for itself, and changes the week before launch without a deploy.

CREATE TABLE applications.form_versions (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id     uuid        NOT NULL REFERENCES applications.events (id) ON DELETE CASCADE,

    -- Ascending per event. This is the number written onto an application.
    version      int         NOT NULL,

    status       text        NOT NULL DEFAULT 'draft'
                             CHECK (status IN ('draft', 'published', 'retired')),

    -- JSON rather than a table per question type, because the shape of a
    -- question differs by type and the whole document is read and written as
    -- one unit — there is no query that wants half a form.
    fields       jsonb       NOT NULL DEFAULT '[]',

    created_by   uuid        REFERENCES identity.people (id),
    created_at   timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz,
    published_by uuid        REFERENCES identity.people (id),

    UNIQUE (event_id, version)
);

-- Exactly one published form per event. Two would mean applicants answering
-- different questions with nothing to say which was current, and the database
-- is the only place that rule cannot be worked around.
CREATE UNIQUE INDEX form_versions_one_published_per_event
    ON applications.form_versions (event_id) WHERE status = 'published';

-- One draft, because more than one turns "publish" into a question about which
-- draft, and nobody wants to answer that at 2am.
CREATE UNIQUE INDEX form_versions_one_draft_per_event
    ON applications.form_versions (event_id) WHERE status = 'draft';

CREATE INDEX form_versions_event_idx ON applications.form_versions (event_id, version DESC);

-- A published form is frozen.
--
-- Enforced here rather than in the code that normally writes it, because an
-- application answering questions that have since changed is not something
-- anybody can detect afterwards. Applications record form_version precisely so
-- an answer can be read against the questions it was actually given, and that
-- guarantee has to hold for a support script at 2am as well.
CREATE FUNCTION applications.freeze_published_forms() RETURNS trigger AS $$
BEGIN
    IF OLD.status = 'published' AND NEW.status = 'published'
       AND NEW.fields IS DISTINCT FROM OLD.fields THEN
        RAISE EXCEPTION
            'form version % is published and cannot be edited; create a new draft',
            OLD.version
            USING ERRCODE = 'restrict_violation';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER form_versions_freeze_published
    BEFORE UPDATE ON applications.form_versions
    FOR EACH ROW EXECUTE FUNCTION applications.freeze_published_forms();
