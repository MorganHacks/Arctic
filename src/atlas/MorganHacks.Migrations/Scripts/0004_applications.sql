-- The application itself: events, applications, the audit trail and internal
-- notes.
--
-- Owned by MorganHacks.Migrations like every other schema. Nothing outside
-- MorganHacks.Applications reads these tables.
--
-- The question set is deliberately absent. Which questions get asked, and
-- which answers are promoted out of `responses` into real columns, is a
-- decision the organizing team has not made yet. Everything here is the part
-- that does not depend on it: the lifecycle, the dedupe rule, the audit trail
-- and the fields MLH affiliation requires regardless of what else we ask.

-- ---------------------------------------------------------------- events ---
-- Everything is scoped to an event. Without this, next year's cycle either
-- wipes this year's data or becomes a second database.
CREATE TABLE applications.events (
    id                     uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    slug                   text        NOT NULL UNIQUE,
    name                   text        NOT NULL,
    starts_at              timestamptz,
    ends_at                timestamptz,
    registration_opens_at  timestamptz,
    registration_closes_at timestamptz,

    -- The target to track `confirmed` against. Never `accepted`, which is
    -- always higher and always a lie.
    capacity               int,

    created_at             timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------- applications ---
-- One row per person per event, created the moment someone starts the form
-- rather than when they submit.
--
-- There is deliberately no separate drafts table. A draft is this row with
-- status 'incomplete'. Two tables would mean copying answers across on submit,
-- which is a thing to get wrong for no benefit.
CREATE TABLE applications.applications (
    id              uuid  PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id        uuid  NOT NULL REFERENCES applications.events (id),

    -- Nullable because someone can begin an application before they have an
    -- identity row, and ON DELETE SET NULL because deleting a person must not
    -- silently delete the record of their application.
    person_id       uuid  REFERENCES identity.people (id) ON DELETE SET NULL,
    email           text  NOT NULL,

    status          text  NOT NULL DEFAULT 'incomplete' CHECK (status IN (
                          'incomplete', 'submitted', 'under_review',
                          'accepted', 'rejected', 'waitlisted',
                          'confirmed', 'declined', 'expired',
                          'checked_in', 'withdrawn')),

    -- Which question set they answered. Together with the agreement
    -- timestamps below this is what proves *what* somebody agreed to.
    form_version    int   NOT NULL DEFAULT 1,

    -- Core fields are real columns because they get filtered, exported or
    -- read at check-in. Everything else lives in `responses`.
    first_name          text,
    last_name           text,
    school              text,
    level_of_study      text,
    graduation_year     int,
    first_time_hacker   boolean,
    shirt_size          text,
    dietary_needs       text,
    accessibility_needs text,
    country             text,

    -- MLH affiliation mandates these, so they are core columns rather than
    -- `responses` keys: they go in the export, and an export that has to dig
    -- through JSON is one someone will eventually get wrong.
    age                  int,
    phone                text,

    -- Timestamps rather than booleans, on purpose. "They agreed" is weaker
    -- evidence than "they agreed at 14:03 on the 12th, against form version
    -- 3", and this is a legal agreement we may have to show.
    mlh_coc_agreed_at    timestamptz,
    mlh_data_sharing_at  timestamptz,
    mlh_marketing_opt_in boolean NOT NULL DEFAULT false,

    -- Everything the form asks that is not promoted above.
    responses       jsonb NOT NULL DEFAULT '{}',

    -- A storage key, never a URL. URLs expire, get copied into emails, and
    -- turn into a way to read somebody's resume without signing in.
    resume_key         text,
    resume_filename    text,
    resume_size        int,
    resume_uploaded_at timestamptz,

    started_at      timestamptz NOT NULL DEFAULT now(),
    submitted_at    timestamptz,
    decided_at      timestamptz,
    decided_by      uuid REFERENCES identity.people (id),

    rsvp_deadline   timestamptz,
    confirmed_at    timestamptz,
    declined_at     timestamptz,

    checked_in_at   timestamptz,
    checked_in_by   uuid REFERENCES identity.people (id),

    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),

    -- Autosave writes partial rows, so these cannot be NOT NULL on the column.
    -- Requiring them from the moment the row stops being a draft gets both:
    -- someone can close the tab halfway through, and a submitted application
    -- missing an MLH-required field cannot exist.
    --
    -- 'withdrawn' is excluded because it is reachable from 'incomplete' —
    -- somebody who never finished can still ask to be removed.
    CONSTRAINT submitted_applications_are_complete CHECK (
        status IN ('incomplete', 'withdrawn')
        OR (first_name IS NOT NULL
            AND last_name IS NOT NULL
            AND age IS NOT NULL
            AND phone IS NOT NULL
            AND school IS NOT NULL
            AND level_of_study IS NOT NULL
            AND country IS NOT NULL
            AND mlh_coc_agreed_at IS NOT NULL
            AND mlh_data_sharing_at IS NOT NULL))
);

-- The dedupe rule, at the database rather than in application code. This is
-- what stops one person applying four times, and it holds regardless of which
-- code path did the insert.
CREATE UNIQUE INDEX applications_event_email_key
    ON applications.applications (event_id, lower(email));

CREATE INDEX applications_event_status_idx
    ON applications.applications (event_id, status);

-- Waitlist ordering and review-queue ordering both read this.
CREATE INDEX applications_event_submitted_idx
    ON applications.applications (event_id, submitted_at);

-- So answers living in `responses` stay filterable without promoting them.
CREATE INDEX applications_responses_gin
    ON applications.applications USING gin (responses);

-- The hourly RSVP expiry job scans this.
CREATE INDEX applications_event_rsvp_idx
    ON applications.applications (event_id, rsvp_deadline);

-- -------------------------------------------------------- status history ---
-- Append-only. One row per transition, never updated and never deleted.
CREATE TABLE applications.status_history (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id uuid NOT NULL
                        REFERENCES applications.applications (id) ON DELETE CASCADE,

    -- Null on the first row, because nothing preceded it.
    from_status    text,
    to_status      text NOT NULL,

    -- Null when the applicant did it themselves, or when it was the system —
    -- the expiry job has no actor and pretending otherwise would put a person's
    -- name against a decision they did not make.
    actor_id       uuid REFERENCES identity.people (id),
    reason         text,

    -- Set when this was part of a bulk action. The piece people leave out:
    -- when someone bulk-accepts four hundred applicants and one was wrong,
    -- this is how you find the others in that action to undo them.
    batch_id       uuid,

    created_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX status_history_application_idx
    ON applications.status_history (application_id, created_at);

CREATE INDEX status_history_batch_idx
    ON applications.status_history (batch_id) WHERE batch_id IS NOT NULL;

-- ----------------------------------------------------------------- notes ---
-- Internal only, never visible to the applicant. Worth saying out loud to
-- reviewers as well as encoding here.
CREATE TABLE applications.notes (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    application_id uuid NOT NULL
                        REFERENCES applications.applications (id) ON DELETE CASCADE,
    author_id      uuid NOT NULL REFERENCES identity.people (id),
    body           text NOT NULL,
    created_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX notes_application_idx ON applications.notes (application_id, created_at);
