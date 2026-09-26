ALTER TABLE applications.announcements
    ADD COLUMN content jsonb,
    ADD CONSTRAINT announcement_content_is_valid CHECK (
        content IS NULL OR (
            jsonb_typeof(content) = 'object'
            AND content ? 'kind'
            AND content->>'kind' IN ('image', 'video', 'poll', 'imagePoll', 'quiz')
            AND octet_length(content::text) <= 20000
        )
    );

CREATE TABLE applications.announcement_responses (
    announcement_id uuid NOT NULL REFERENCES applications.announcements (id),
    person_id uuid NOT NULL REFERENCES identity.people (id),
    choice smallint NOT NULL CHECK (choice BETWEEN 0 AND 3),
    answered_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (announcement_id, person_id)
);
