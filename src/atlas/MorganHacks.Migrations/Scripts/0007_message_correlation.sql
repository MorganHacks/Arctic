-- The correlation id, carried onto the message.
--
-- This is what closes the loop on "I never got my sign-in link". The id starts
-- at harbor, reaches atlas on a header, and stops there — so the request that
-- queued a message and the worker that sent it have no shared handle, and
-- answering that question means lining up timestamps by hand.
--
-- Stamped on the row rather than only logged, because the send happens minutes
-- later in a different process. A log line alone would tie the request to the
-- queueing and nothing after it.
ALTER TABLE notify.messages ADD COLUMN correlation_id text;

CREATE INDEX messages_correlation_idx ON notify.messages (correlation_id)
    WHERE correlation_id IS NOT NULL;
