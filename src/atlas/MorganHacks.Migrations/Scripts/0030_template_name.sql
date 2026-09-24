ALTER TABLE notify.templates
    ADD COLUMN name text CHECK (
        char_length(name) BETWEEN 1 AND 200 AND btrim(name) <> ''
    );
