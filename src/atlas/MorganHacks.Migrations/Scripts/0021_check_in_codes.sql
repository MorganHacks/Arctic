-- The code a hacker shows at the door.
--
-- Two columns on the application rather than a table of their own. A check-in
-- code is one-to-one with an application, is issued once and never rotates,
-- and dies with the row it belongs to. A second table would buy a join on the
-- one query that has to answer in front of a queue.

-- ---------------------------------------------------------------- the code ---
-- Stored as the code itself, not as a hash, and that is the decision in this
-- file worth arguing about.
--
-- Every other secret in this system is hashed: sessions and magic links are
-- both stored as SHA-256 because a leak of that table would otherwise hand out
-- live logins. This one is different in the way that matters. It authenticates
-- nothing. Presenting it does not sign anybody in, does not read anybody's
-- application and does not move anybody's status by itself: it can only be
-- redeemed by an organizer who already holds checkin.scan, standing in front
-- of the person, and all it does then is record that they arrived.
--
-- Against that, the code has to be readable back. The hacker sees it in their
-- portal, screenshots it on the bus, and shows the screenshot at the door with
-- no signal. That only works if the same code comes back every time they open
-- the page, and a column we can only compare against cannot be shown again.
--
-- Twelve characters of Crockford base32, which is sixty bits. Guessing one is
-- not the attack anybody would choose even with the permission in hand, and
-- the alphabet has no I, L, O or U in it, so nothing here is mistaken for
-- something else when it is read aloud across a noisy room.
ALTER TABLE applications.applications ADD COLUMN check_in_code text;

-- When it was minted. Not the same fact as checked_in_at, which is when it was
-- used, and the gap between the two is how you tell somebody who saved their
-- code a week early from somebody who opened the portal in the queue.
ALTER TABLE applications.applications ADD COLUMN check_in_code_issued_at timestamptz;

-- Unique across the table rather than per event, because the scan has no event
-- to scope by. A volunteer holds a phone and a code and nothing else, so the
-- code alone has to name exactly one application or the endpoint would need to
-- guess which year somebody meant.
--
-- Partial: most rows never get a code, because only somebody who confirmed a
-- spot has anything to show.
CREATE UNIQUE INDEX applications_check_in_code_key
    ON applications.applications (check_in_code)
 WHERE check_in_code IS NOT NULL;

-- The shape, at the database rather than only in C#.
--
-- Same reasoning as the slug constraint in 0020: this is exactly the rule a
-- hand-written UPDATE during the event skips, and a code with a lowercase
-- letter or a stray dash in it is one the scan endpoint would normalise past
-- and then fail to find.
ALTER TABLE applications.applications
    ADD CONSTRAINT check_in_code_is_canonical
    CHECK (check_in_code IS NULL OR check_in_code ~ '^[0-9A-HJKMNP-TV-Z]{12}$');
