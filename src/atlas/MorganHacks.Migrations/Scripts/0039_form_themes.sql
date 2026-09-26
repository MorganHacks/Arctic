ALTER TABLE applications.form_versions
    ADD COLUMN theme jsonb NOT NULL DEFAULT '{}',
    ADD CONSTRAINT form_theme_object CHECK (jsonb_typeof(theme) = 'object');

CREATE OR REPLACE FUNCTION applications.freeze_published_forms() RETURNS trigger AS $$
BEGIN
    IF OLD.status = 'published' AND NEW.status = 'published'
       AND (NEW.fields IS DISTINCT FROM OLD.fields OR NEW.theme IS DISTINCT FROM OLD.theme) THEN
        RAISE EXCEPTION
            'form version % is published and cannot be edited; create a new draft',
            OLD.version
            USING ERRCODE = 'restrict_violation';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
