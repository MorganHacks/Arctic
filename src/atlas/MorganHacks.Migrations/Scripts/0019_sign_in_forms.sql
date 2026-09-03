-- Forms for people we already have on file.
--
-- The application form is the first thing a stranger does, and it stays
-- unauthenticated: requiring an account before applying would be a sign-up
-- before the sign-up. Everything else on the builder is answered by somebody
-- who already applied — a mentor sign-up, an RSVP, a post-event survey — and
-- for those, asking for an email again is friction and a data problem. They
-- typo it, or use the address they no longer read, and the answer cannot be
-- joined to the application it is about.
--
-- So audience becomes a property of the form rather than a property of the
-- site. Two columns, because "must you sign in" and "which of the signed-in
-- may answer" are genuinely different questions: an RSVP is for `accepted`
-- and a feedback survey for `checked_in`, and nothing global gets that right
-- for both.

-- ------------------------------------------------------------ the audience ---
ALTER TABLE applications.forms
    ADD COLUMN requires_sign_in boolean NOT NULL DEFAULT false,

    -- Empty for every form that does not require sign-in, and never empty for
    -- one that does. See the constraints below: an empty list on a gated form
    -- has two plausible readings — nobody, or everybody — and a column whose
    -- meaning has to be guessed is one that eventually gets guessed wrong on
    -- the form that decides who gets fed.
    ADD COLUMN eligible_statuses text[] NOT NULL DEFAULT '{}';

-- The rule that must never bend. Gating `application` behind sign-in makes
-- applying impossible, because the account it would demand is created by
-- applying.
--
-- Here rather than only in the endpoint, because this is the kind of setting
-- somebody eventually changes with an UPDATE at 2am during registration week.
ALTER TABLE applications.forms
    ADD CONSTRAINT the_application_form_is_never_gated
    CHECK (kind <> 'application' OR requires_sign_in = false);

-- An audience only means something on a form that has one. Statuses left
-- behind on a form whose gate was turned off would come back the day somebody
-- turned it on again, silently narrowing who may answer.
ALTER TABLE applications.forms
    ADD CONSTRAINT eligible_statuses_belong_to_gated_forms
    CHECK (requires_sign_in OR cardinality(eligible_statuses) = 0);

ALTER TABLE applications.forms
    ADD CONSTRAINT a_gated_form_names_its_audience
    CHECK (NOT requires_sign_in OR cardinality(eligible_statuses) > 0);

-- The same eleven the applications table checks, spelled the same way. A
-- containment check rather than a trigger, so a status that does not exist is
-- refused at the moment somebody saves the form rather than discovered when
-- nobody turns out to be eligible.
ALTER TABLE applications.forms
    ADD CONSTRAINT eligible_statuses_are_real_statuses
    CHECK (eligible_statuses <@ ARRAY[
        'incomplete', 'submitted', 'under_review',
        'accepted', 'rejected', 'waitlisted',
        'confirmed', 'declined', 'expired',
        'checked_in', 'withdrawn']::text[]);

-- ------------------------------------------------------ where answers land ---
-- Until now only the application form persisted anything: `submit` on a survey
-- answered 501, because there was nowhere to put the answers and returning 200
-- and dropping them would have been the worst of the options — somebody would
-- believe they had replied.
--
-- A sign-in form cannot ship without fixing that, and the fix is finally
-- available precisely because these forms are signed in. The reason a survey
-- had nowhere to go was never storage; it was that an anonymous answer has no
-- key. There is no person to file it under, no rule about whether the same
-- person may answer twice, and no way to tell two submissions from one person
-- changing their mind. Signing in answers all three at once.
--
-- So this table is deliberately only for answers that belong to somebody.
-- `person_id` is NOT NULL and that is the whole design: an anonymous survey
-- still has nowhere to land, still answers 501, and still has that decision
-- ahead of it.
CREATE TABLE applications.form_submissions (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    form_id      uuid        NOT NULL REFERENCES applications.forms (id) ON DELETE CASCADE,

    -- Which questions they were shown. The same reason the applications table
    -- carries one: an answer read a year later is unreadable without the
    -- question, and the question changes.
    form_version int         NOT NULL,

    person_id    uuid        NOT NULL REFERENCES identity.people (id) ON DELETE CASCADE,

    -- The application this was answered against, so an RSVP can be read
    -- beside the application it is an RSVP to. Nullable and ON DELETE SET
    -- NULL for the same reason applications.person_id is: deleting one record
    -- must not silently delete the other.
    application_id uuid      REFERENCES applications.applications (id) ON DELETE SET NULL,

    -- Keyed by FormField.Key, like applications.responses. Nothing is promoted
    -- to a column here: a survey answer is not filtered, exported at check-in
    -- or read on a badge, and a column per question per survey per year is the
    -- shape this schema already decided against once.
    answers      jsonb       NOT NULL DEFAULT '{}',

    submitted_at timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now()
);

-- One answer per person per form, enforced here rather than in code.
--
-- An RSVP somebody changes their mind about is an update, not a second row:
-- the question "are you coming" has one current answer, and two rows means
-- every reader has to decide which of them counts. The submit path upserts
-- against this index, which is also what makes a double-tapped Submit on a
-- slow phone harmless.
CREATE UNIQUE INDEX form_submissions_form_person_key
    ON applications.form_submissions (form_id, person_id);

CREATE INDEX form_submissions_form_idx
    ON applications.form_submissions (form_id, submitted_at DESC);

CREATE INDEX form_submissions_person_idx
    ON applications.form_submissions (person_id);

-- ------------------------------------------------- finding who is on file ---
-- The email step looks an address up against the applications of one event,
-- and must answer in the same time whether or not it finds one. The dedupe
-- index is on (event_id, lower(email)) already, so that lookup is covered —
-- this is the other direction, used once the link has been clicked and the
-- session is being checked against the form's audience.
--
-- applications_person_idx from 0011 is on person_id alone, which serves the
-- portal's "my application" but not this one's "my application for this
-- event". The event is what a form is scoped to.
CREATE INDEX applications_event_person_idx
    ON applications.applications (event_id, person_id) WHERE person_id IS NOT NULL;
