-- What a broadcast needs that 0003 did not have.
--
-- Two changes, both of them about the same failure: a blast that goes to the
-- same person twice, or that cannot be stopped once it has been queued. Several
-- hundred people receiving a duplicate is the one mistake here that cannot be
-- taken back, so both rules belong in the database rather than in the endpoint
-- that happens to write these rows today.

-- ------------------------------------------------- the dedupe key that holds ---
-- 0003 put a unique index on (campaign_id, person_id) and called it "the whole
-- duplicate-prevention story". It is, for a campaign whose recipients are all
-- people we have rows for — and it silently is not for one whose recipients are
-- not.
--
-- NULLs do not conflict in a Postgres unique index. A segment that is a typed
-- list of addresses resolves to rows with person_id NULL, so pressing send
-- twice would insert the second copy without complaint and mail everybody
-- again. That is exactly the failure the original index was written to stop,
-- reached through the one door it does not cover.
--
-- The address is the key that always exists, because it is the thing being
-- mailed. citext, so a person who typed their address with a capital letter on
-- the form and lower case in a spreadsheet is one recipient rather than two.
--
-- Safe to add to existing data: the only writer today is
-- EnqueueTransactionalAsync, which makes one campaign per message, so no
-- campaign in flight has two rows for one address.
CREATE UNIQUE INDEX messages_campaign_email_key
    ON notify.messages (campaign_id, to_email);

-- ------------------------------------------------------- stopping a broadcast ---
-- 'cancelled' as a message status, so a campaign that is called off can say
-- what happened to the rows that never went.
--
-- The alternatives are both worse. Deleting them destroys the frozen recipient
-- list, which is the reason the rows exist — "who exactly did we email" has to
-- stay answerable, and so does "who were we about to". Reusing 'suppressed'
-- would be a lie in the one table people read when an address stops receiving
-- mail: nothing about this address was suppressed, somebody stopped the
-- campaign.
--
-- Nothing claims these. The claim query in MessageQueue takes 'pending' only,
-- so a cancelled row is out of the queue the moment it is written.
ALTER TABLE notify.messages DROP CONSTRAINT messages_status_check;

ALTER TABLE notify.messages ADD CONSTRAINT messages_status_check
    CHECK (status IN ('pending','sending','sent','delivered',
                      'bounced','complained','failed_temp',
                      'failed_perm','suppressed','cancelled'));
