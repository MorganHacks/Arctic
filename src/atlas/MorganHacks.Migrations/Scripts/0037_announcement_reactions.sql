CREATE TABLE applications.announcement_reactions (
    announcement_id uuid NOT NULL REFERENCES applications.announcements (id),
    person_id uuid NOT NULL REFERENCES identity.people (id),
    reaction text NOT NULL CHECK (reaction IN ('love', 'wow', 'confused', 'support', 'happy')),
    reacted_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (announcement_id, person_id)
);
