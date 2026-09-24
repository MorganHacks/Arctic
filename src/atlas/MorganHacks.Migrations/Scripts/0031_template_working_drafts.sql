CREATE TABLE notify.template_working_drafts (
    key text NOT NULL CHECK (char_length(key) BETWEEN 1 AND 64),
    author uuid NOT NULL REFERENCES identity.people (id),
    content jsonb NOT NULL,
    base_version integer CHECK (base_version > 0),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (key, author)
);
