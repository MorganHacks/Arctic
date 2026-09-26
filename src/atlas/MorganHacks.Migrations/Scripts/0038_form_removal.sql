ALTER TABLE applications.forms ADD COLUMN removed_at timestamptz;

DROP INDEX applications.forms_one_application_per_event;
CREATE UNIQUE INDEX forms_one_application_per_event
    ON applications.forms (event_id)
    WHERE kind = 'application' AND removed_at IS NULL;
