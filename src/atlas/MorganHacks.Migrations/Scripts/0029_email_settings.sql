ALTER TABLE notify.templates
    ADD COLUMN preview_text text CHECK (char_length(preview_text) <= 200),
    ADD COLUMN click_tracking boolean NOT NULL DEFAULT false;

CREATE TABLE notify.tracked_links (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    message_id uuid NOT NULL REFERENCES notify.messages(id) ON DELETE CASCADE,
    destination text NOT NULL CHECK (char_length(destination) <= 4096),
    destination_hash bytea NOT NULL,
    click_count bigint NOT NULL DEFAULT 0,
    first_clicked_at timestamptz,
    last_clicked_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (message_id, destination_hash)
);
