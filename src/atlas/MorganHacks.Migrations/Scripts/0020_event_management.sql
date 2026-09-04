-- What making an event needs that did not exist yet.
--
-- Until now an event was made by hand, in psql, once a year. That is why
-- staging has no event and a laptop does: somebody inserted one locally while
-- testing and nothing else ever did. Three small things here, together because
-- they exist for one reason — an organizer creating the year's event through
-- the console instead.

-- ----------------------------------------------------- who may make one ---
-- Its own permission rather than reusing people.grant_permissions or
-- audit.view, which are the two nobody but super admin holds.
--
-- Reuse is tempting because the audience is the same today, and it is wrong
-- for the same reason applications.view_responses is not applications.view:
-- the permission somebody holds is the sentence the console shows them and the
-- string an admin reads on a grant screen. "Give them people.grant_permissions
-- so they can set the registration dates" hands out the ability to change
-- everybody's access to get a date field, and that is not a trade anybody
-- would make deliberately — it is one they make because the permission was
-- named after the wrong thing.
--
-- Super admin only, and nobody else by default. There is one event a year, it
-- is the root that forms, applications and campaign segments all hang off, and
-- changing its registration dates changes who can apply. Anybody else who
-- needs it gets an individual grant, which is what grants are for.
--
-- Not on the sensitive list in Permission.cs. That list is the four that move
-- PII out of the system or change who is allowed to; making an event does
-- neither.
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, 'events.manage' FROM identity.teams
 WHERE slug = 'super-admin'
    ON CONFLICT DO NOTHING;

-- ------------------------------------------------------- what a slug is ---
-- The column has been NOT NULL UNIQUE since 0004 and nothing has ever said
-- what may go in it. A slug is an identifier people type and paste — mh2027 —
-- and the two ways it breaks are a slash, which silently becomes another path
-- segment wherever it lands in a URL, and case, because 'MH2027' and 'mh2027'
-- are two rows to this index and one link to a person.
--
-- Here rather than only in C# for the reason the dedupe index in 0004 is here:
-- this is exactly the rule a hand-written INSERT during the event will skip,
-- and hand-written INSERTs are how every event so far has been made.
--
-- NOT VALID, so existing rows are left alone. Someone's laptop already holds
-- an event slugged whatever they typed in March, and a migration that refuses
-- to run there teaches people to skip migrations. New rows are checked from
-- this moment, which is the part that matters.
ALTER TABLE applications.events
    ADD CONSTRAINT events_slug_is_urlsafe
    CHECK (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(slug) BETWEEN 2 AND 40)
    NOT VALID;

-- ------------------------------------------------------- who made one ---
-- applications.forms has carried created_by since 0009 and an event is the
-- more consequential of the two. Nullable, because every event that exists
-- today was inserted by a person at a psql prompt and there is no honest id to
-- put against it — a name attached to a decision nobody recorded is worse than
-- an empty column.
--
-- ON DELETE is deliberately absent, matching applications.forms: removing an
-- organizer must not quietly rewrite the record of what they did.
ALTER TABLE applications.events
    ADD COLUMN created_by uuid REFERENCES identity.people (id);
