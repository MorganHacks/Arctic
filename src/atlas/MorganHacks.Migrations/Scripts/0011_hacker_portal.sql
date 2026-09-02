-- What the hacker portal needs that did not exist yet.
--
-- Three small things, together because they exist for one reason: an applicant
-- signing in and being shown their own application. Nothing here changes what
-- any existing row means.

-- ------------------------------------------------ when decisions are public ---
-- ApplicantView.Describe already refuses to show a decision until decisions
-- have been announced. Nothing supplied that flag, so the parameter defaulted
-- to false forever and there was no way to ever release results short of a
-- deploy.
--
-- Per event rather than global, because that is the scope the decision has:
-- announcing 2027 must not reveal anything about a 2028 cycle running
-- alongside it in the same database.
--
-- A timestamp rather than a boolean, like every other consequential flag here.
-- "Decisions are out" is weaker than "they went out at 18:00 on the 4th", and
-- that is the first question asked when somebody says they were told before
-- everyone else.
ALTER TABLE applications.events ADD COLUMN decisions_announced_at timestamptz;

-- -------------------------------------------- finding your own application ---
-- Every portal query starts from the session's person_id. Without this index
-- that is a sequential scan of every application in the event, on the request
-- that renders the applicant's first screen.
--
-- Partial, because a row with no person_id is one nobody can sign in and claim
-- — it is reachable only by an organizer, through a different query.
CREATE INDEX applications_person_idx
    ON applications.applications (person_id) WHERE person_id IS NOT NULL;

-- ------------------------------------------------------- "I never got it" ---
-- The message history the portal shows. messages_campaign_person_key is
-- (campaign_id, person_id), so it cannot serve a lookup by person alone —
-- the leading column is the one the portal does not have.
--
-- Descending on created_at because the screen is newest-first and there is no
-- reason to sort several hundred rows per view.
CREATE INDEX messages_person_idx
    ON notify.messages (person_id, created_at DESC) WHERE person_id IS NOT NULL;
