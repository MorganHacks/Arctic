-- lark's tables.
--
-- Owned by MorganHacks.Migrations like every other schema: lark never migrates
-- its own tables from its own process. Two services racing to migrate one
-- database is the documented way setups like this break.

CREATE EXTENSION IF NOT EXISTS citext;

-- --------------------------------------------------------------- templates ---
-- `kind` drives both the lane and the sending subdomain. It lives on the
-- template rather than being passed per send, because getting it wrong is what
-- poisons login deliverability, and a per-send argument is something a caller
-- can get wrong once.
CREATE TABLE notify.templates (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    key          text        NOT NULL UNIQUE,
    kind         text        NOT NULL CHECK (kind IN ('transactional', 'broadcast')),
    subject      text        NOT NULL,
    body_html    text        NOT NULL,
    -- Always both. Text-only clients exist, and a text part improves how
    -- spam filters score the message.
    body_text    text        NOT NULL,
    from_local   text        NOT NULL,
    from_domain  text        NOT NULL,
    reply_to     text,
    version      int         NOT NULL DEFAULT 1,
    created_at   timestamptz NOT NULL DEFAULT now()
);

-- --------------------------------------------------------------- campaigns ---
-- The intent. One row however many recipients it resolves to.
CREATE TABLE notify.campaigns (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id        uuid,
    template_id     uuid        NOT NULL REFERENCES notify.templates (id),

    -- Stored, not just executed. A month later "who exactly did we email" has
    -- to have an answer, and re-running the filter gives a different one
    -- because the data moved underneath it.
    segment         jsonb,

    name            text        NOT NULL,
    status          text        NOT NULL DEFAULT 'draft'
                    CHECK (status IN ('draft','queued','sending','sent','cancelled','failed')),
    recipient_count int         NOT NULL DEFAULT 0,
    created_by      uuid        REFERENCES identity.people (id),
    -- Broadcasts only. Transactional sends have no approver.
    approved_by     uuid        REFERENCES identity.people (id),
    queued_at       timestamptz,
    completed_at    timestamptz,
    created_at      timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------- messages ---
-- One row per recipient, written before anything sends.
--
-- The naive version loops over recipients calling the provider, and fails the
-- first time the process is rescheduled mid-blast: you cannot tell who
-- received it, and both resuming and restarting are wrong. Rows survive the
-- process, so a restart resumes exactly where it stopped.
CREATE TABLE notify.messages (
    id                  uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    campaign_id         uuid        NOT NULL REFERENCES notify.campaigns (id) ON DELETE CASCADE,
    person_id           uuid        REFERENCES identity.people (id) ON DELETE SET NULL,
    to_email            citext      NOT NULL,

    -- 0 transactional, 10 broadcast. Ascending, so a login link never queues
    -- behind two thousand announcements.
    priority            smallint    NOT NULL DEFAULT 10,

    status              text        NOT NULL DEFAULT 'pending'
                        CHECK (status IN ('pending','sending','sent','delivered',
                                          'bounced','complained','failed_temp',
                                          'failed_perm','suppressed')),
    attempts            smallint    NOT NULL DEFAULT 0,
    next_attempt_at     timestamptz,
    provider_message_id text,
    last_error          text,

    locked_by           text,
    locked_until        timestamptz,

    -- Rendered once, at queue time. If someone's name changes between queue
    -- and send the email should say what it said when it was approved, and a
    -- retry must not render differently from the original attempt.
    rendered_subject    text        NOT NULL,
    rendered_body_html  text        NOT NULL,
    rendered_body_text  text        NOT NULL,

    created_at          timestamptz NOT NULL DEFAULT now(),
    sent_at             timestamptz
);

-- The whole duplicate-prevention story. Queue a campaign twice and the second
-- insert conflicts rather than sending twice. Enforced here, never in code.
CREATE UNIQUE INDEX messages_campaign_person_key
    ON notify.messages (campaign_id, person_id);

-- The claim query's index. Order matches ORDER BY priority, created_at.
CREATE INDEX messages_claim_idx
    ON notify.messages (status, priority, created_at)
    WHERE status = 'pending';

CREATE INDEX messages_provider_id_idx ON notify.messages (provider_message_id)
    WHERE provider_message_id IS NOT NULL;

-- For the sweeper that recovers rows whose worker died mid-claim.
CREATE INDEX messages_lock_idx ON notify.messages (locked_until)
    WHERE status = 'sending';

-- ------------------------------------------------------------ suppressions ---
-- Checked before every send, no exceptions.
--
-- A hard bounce is respected in both lanes, because a dead address is dead
-- either way. An unsubscribe applies to broadcast only: someone who opted out
-- of announcements must still get their login link and their decision, which
-- they asked for by acting.
CREATE TABLE notify.suppressions (
    email       citext      PRIMARY KEY,
    reason      text        NOT NULL
                CHECK (reason IN ('hard_bounce','complaint','unsubscribed','manual')),
    campaign_id uuid        REFERENCES notify.campaigns (id) ON DELETE SET NULL,
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- ------------------------------------------------------------------- state ---
-- One row, so every replica agrees about whether sending is paused. The
-- circuit breaker writes here.
CREATE TABLE notify.state (
    id                  boolean     PRIMARY KEY DEFAULT true CHECK (id),
    broadcast_paused    boolean     NOT NULL DEFAULT false,
    paused_reason       text,
    paused_at           timestamptz,
    paused_by           uuid        REFERENCES identity.people (id)
);

INSERT INTO notify.state (id) VALUES (true);
