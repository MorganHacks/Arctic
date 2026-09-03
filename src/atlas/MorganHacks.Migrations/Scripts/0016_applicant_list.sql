-- The indexes the applicant list reads.
--
-- Nothing changes shape here. The list, the filters and the notes all run
-- against tables 0004 already built; what was missing was an ordering the
-- database can walk rather than sort.
--
-- The list is paged by keyset on (created_at, id) descending, scoped to an
-- event and optionally to a set of statuses. Without an index in that order
-- every page is a full read of the event followed by a top-N sort — which is
-- survivable at a few thousand rows and survivable in the worst way, because
-- the cost of a page grows with how far down somebody has scrolled. Paying the
-- whole scan again to fetch rows 500-550 is exactly what keyset paging exists
-- to avoid, and an index is what makes it actually avoid it.

-- created_at rather than submitted_at, which is what the responses list orders
-- by. That list is only submitted applications; this one is every applicant
-- including the ones who closed the tab half-way down, and those have no
-- submitted_at at all. A nullable sort key is a cursor that cannot name half
-- the rows it is meant to page through.
CREATE INDEX applications_event_created_idx
    ON applications.applications (event_id, created_at DESC, id DESC);

-- Replacing applications_event_status_idx rather than sitting beside it.
--
-- That index is (event_id, status), which is a strict prefix of this one, so
-- everything it answered this answers too — the status counts above the list
-- included. Two indexes where one will do is two indexes to write on every
-- insert, during the hour every application of the year arrives.
DROP INDEX applications.applications_event_status_idx;

CREATE INDEX applications_event_status_created_idx
    ON applications.applications (event_id, status, created_at DESC, id DESC);

-- Search is deliberately not indexed.
--
-- Registration searches by a fragment of a name or an address, which means a
-- leading wildcard, which no btree can serve. The thing that would is a
-- trigram GIN index, and pg_trgm is an extension — every one of those has to
-- be allow-listed on the Azure server before it can even be created, which is
-- a request to somebody else and a deploy that fails in an environment nobody
-- tested. See 0009, which chose md5 over pgcrypto's digest for the same
-- reason.
--
-- What is left is a scan of one event's applications, which is a few thousand
-- rows in the year this is being written for. That is milliseconds, and it is
-- honest about being milliseconds because of the event filter above it: the
-- scan is bounded by one cycle rather than by everything we have ever stored.
-- The day an event is large enough for that to matter, the fix is the
-- extension and this comment is where to start.
