ALTER TABLE applications.form_versions
    ADD COLUMN updated_at timestamptz,
    ADD COLUMN updated_by uuid REFERENCES identity.people (id);
