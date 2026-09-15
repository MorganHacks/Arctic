-- Answers to a form that nobody had to sign in to fill in.
--
-- 0019 created applications.form_submissions for signed-in answers and said so
-- in its own comment: person_id was NOT NULL and "an anonymous survey still has
-- nowhere to land, still answers 501, and still has that decision ahead of it".
-- This is that decision.
--
-- The argument in 0019 was that an anonymous answer has no key -- no person to
-- file it under, no rule about answering twice, and no way to tell two
-- submissions from one person changing their mind. That was right about the
-- last two and wrong about the first: the answer does not need a key in order
-- to be kept, it needs one in order to be *deduplicated*, and those are
-- separate problems with separate answers below.
--
-- ------------------------------------------------ one table, not two ---
--
-- The obvious alternative was a second table for answers with no person on
-- them. It was not taken, and the reason is the reader rather than the writer.
--
-- An organizer opening a survey wants the survey: every answer, in one list,
-- in one order, paginated by one cursor. Two tables means every read is a
-- UNION whose ORDER BY and (submitted_at, id) cursor have to stay comparable
-- across both halves, every index has to be created twice, and every future
-- column has to be added twice. The failure that costs is not a crash -- it is
-- a screen that silently shows half the responses, which is exactly the bug
-- PR #111 had just finished fixing in the other direction. A screen that shows
-- half is worse than one that shows none, because nothing on it says a half is
-- missing.
--
-- One table with a nullable person_id makes "every answer to this form" the
-- query it already was: WHERE form_id = $1. The reader added in PR #111 needs
-- no new branch at all, which is the strongest evidence this is the right
-- shape.
ALTER TABLE applications.form_submissions
    ALTER COLUMN person_id DROP NOT NULL;

-- ----------------------------------------- what a second submission means ---
--
-- For a signed-in form the answer is settled: the same person answering again
-- has changed their mind, so it is an update. There is one current answer to
-- "are you coming", and two rows would make every reader decide which counts.
--
-- Anonymously there is nobody to compare against, and every available guess is
-- wrong in a way that loses somebody's words:
--
--   * Same client address -- a lecture theatre is one NAT. Sixty people
--     answering a feedback survey at the end of a talk would come out as one
--     answer, and the fifty-nine discarded would be discarded silently.
--   * Same answers -- on a survey asking "did you enjoy it? yes/no" two
--     genuine people collide constantly. Collapsing them undercounts the
--     result the survey exists to produce.
--
-- So the rule is: an anonymous submission is its own response. Nothing is
-- collapsed on a guess, and nothing is ever dropped.
--
-- That leaves the one duplicate that is not a guess: the same submission
-- arriving twice. A double-tapped Submit on a slow phone, a retry after a
-- response that never came back. The page mints a random key once per
-- submission attempt and sends it with the answers, so two taps carry the same
-- key and two people carry different ones -- because they loaded the page
-- separately. That is an exact test rather than a heuristic, which is the
-- whole reason it is worth a column.
--
-- Nullable, because a caller that sends no key still has to be accepted. A
-- submission with no key is never collapsed with anything, which is the safe
-- way round: the cost is a duplicate row somebody can see and delete, and the
-- cost of the other way round is an answer that was never stored.
ALTER TABLE applications.form_submissions
    ADD COLUMN submission_key uuid;

-- The uniqueness from 0019, narrowed to the rows it was ever about.
--
-- It has to be replaced rather than left alone: a plain unique index treats
-- NULLs as distinct, so it would not refuse anything anonymous -- but the
-- submit path infers its arbiter from this index by name-free column match,
-- and an ON CONFLICT (form_id, person_id) against an index that now covers
-- rows with no person is a statement Postgres refuses to plan. See
-- PostgresRespondentStore, which carries the matching WHERE clause.
DROP INDEX applications.form_submissions_form_person_key;

CREATE UNIQUE INDEX form_submissions_form_person_key
    ON applications.form_submissions (form_id, person_id)
    WHERE person_id IS NOT NULL;

-- One row per submission attempt, enforced here rather than in code, for the
-- same reason the person index is: the retry that matters arrives while the
-- first one is still in flight, and two requests that both read-then-wrote
-- would both find nothing.
CREATE UNIQUE INDEX form_submissions_form_attempt_key
    ON applications.form_submissions (form_id, submission_key)
    WHERE submission_key IS NOT NULL;

-- A key is the anonymous row's substitute for a person, so carrying both would
-- be two deduplication rules on one row with nothing to say which wins. A
-- signed-in answer is deduplicated by who gave it and never by what the
-- browser called the attempt.
ALTER TABLE applications.form_submissions
    ADD CONSTRAINT a_submission_key_stands_in_for_a_person
    CHECK (person_id IS NULL OR submission_key IS NULL);

-- An anonymous answer is not about an application, because we do not know
-- whose it would be. Without this, a write that set application_id and left
-- person_id null would file somebody's application against an answer nobody
-- can be shown to have given -- which reads on screen as attribution and is
-- not one.
ALTER TABLE applications.form_submissions
    ADD CONSTRAINT an_anonymous_answer_is_about_no_application
    CHECK (person_id IS NOT NULL OR application_id IS NULL);
