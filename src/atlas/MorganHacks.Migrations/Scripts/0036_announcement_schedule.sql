ALTER TABLE applications.announcements ADD COLUMN publish_at timestamptz;
UPDATE applications.announcements SET publish_at = posted_at;
ALTER TABLE applications.announcements
    ALTER COLUMN publish_at SET NOT NULL,
    ALTER COLUMN publish_at SET DEFAULT now();

CREATE INDEX ix_announcements_published
    ON applications.announcements (event_id, publish_at DESC, id DESC)
    WHERE retracted_at IS NULL;
