CREATE TABLE notify.hidden_templates (
    person_id uuid NOT NULL REFERENCES identity.people (id) ON DELETE CASCADE,
    template_key text NOT NULL CHECK (char_length(template_key) BETWEEN 1 AND 64),
    hidden_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (person_id, template_key)
);
