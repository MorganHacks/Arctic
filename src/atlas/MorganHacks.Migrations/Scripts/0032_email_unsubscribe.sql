CREATE TABLE notify.unsubscribe_links (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    email citext NOT NULL UNIQUE,
    created_at timestamptz NOT NULL DEFAULT now()
);
