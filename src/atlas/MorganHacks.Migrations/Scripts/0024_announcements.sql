-- The short notice that is not an email.
--
-- During the weekend somebody has to be able to say "judging has moved" and
-- have every hacker in the building see it. Today the only way to tell
-- everybody anything is notify.campaigns, which mails several hundred people,
-- cannot be recalled once lark starts draining the queue, and takes minutes to
-- arrive. Nobody sends one to move a session by two hours, so instead it gets
-- shouted across a room and half the floor never hears it.
--
-- This is the other half of that: a line of text an organizer posts and the
-- portal shows. It sends nothing, so it costs nothing to post and nothing to
-- take back down, which is the entire reason it can be used for the small
-- corrections a broadcast is too heavy for.

-- ------------------------------------------------------- who may post one ---
-- Its own permission rather than a reuse of email.send_broadcast or
-- events.manage, which are the two that already mean "you may speak to
-- everybody" and "you own the season".
--
-- Not events.manage, for the reason 0020 gave for not folding that one into
-- people.grant_permissions: a permission is the sentence somebody reads on a
-- grant screen, and "give them events.manage so they can post that judging
-- moved" hands over the registration dates the whole season hangs off in order
-- to get a line of text onto a screen. Super admin holds events.manage and
-- nobody else does, which during the weekend means the one person who can post
-- a notice is the one person least likely to be standing on the floor.
--
-- Not email.send_broadcast either, and that is the closer call, because the
-- act really is the same act — telling every hacker one thing at once. The
-- difference is what it costs to be wrong. A broadcast is irrevocable the
-- moment it is approved; this is a row somebody sets retracted_at on. Gating
-- the cheap, reversible one behind the permission that exists because the
-- expensive, irreversible one needs a confirmation step means the reversible
-- thing inherits a ceremony it does not need, and it means nobody holds it who
-- is not also trusted to mail four hundred people.
--
-- Posting and retracting are one permission rather than two. Somebody who can
-- put a sentence in front of every hacker and cannot take it back down again
-- is worse off than somebody who holds neither: the mistake they make in the
-- first minute stays on the screen until they find somebody else.
--
-- Three teams, and the third is the one worth arguing about:
--
--   super-admin, which holds everything.
--
--   comms, because they already hold email.send_broadcast — this is the same
--   job one step lighter, and a team trusted to mail everybody is trusted to
--   post to everybody.
--
--   logistics, because they are the team standing in the room when the
--   schedule moves. They already hold checkin.scan and applications.view and
--   they are who actually knows at 1:55pm that judging is not starting at two.
--   A permission model where they have to find a comms person before the
--   screen can say so is a model that gets routed around by shouting, which is
--   the failure this table exists to fix.
--
-- Not on the sensitive list in Permission.cs. That list is the four that move
-- PII out of the system or change who may do so, and this does neither. It
-- does put words in front of everybody, which is why it is not folded into
-- applications.view — that is a large group, and this is not.
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, 'announcements.post' FROM identity.teams
 WHERE slug IN ('super-admin', 'comms', 'logistics')
    ON CONFLICT DO NOTHING;

-- ------------------------------------------------------------ the notices ---
CREATE TABLE applications.announcements (
    id         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Scoped to an event, like everything else in this schema, and NOT NULL
    -- unlike most of what hangs off it. There is no such thing as an
    -- announcement about no particular hackathon: "judging has moved" is only
    -- true of one weekend, and a row without an event is one that would
    -- eventually be shown to next year's applicants.
    --
    -- Nothing happens to these rows when the event ends. They are not swept,
    -- expired or deleted — nothing in this system deletes — because the feed
    -- is scoped by the event of the reader's own application, so last year's
    -- notices stop being visible the moment somebody's current application is
    -- against a different event. An expiry job would be a second mechanism
    -- doing what the WHERE clause already does, and one that could be wrong.
    event_id   uuid        NOT NULL REFERENCES applications.events (id),

    -- The whole of the notice. No title, and that is deliberate rather than
    -- unfinished: what this is for is one sentence, and a title field is an
    -- invitation to write a paragraph underneath it that nobody standing in a
    -- corridor is going to read.
    body       text        NOT NULL,

    posted_at  timestamptz NOT NULL DEFAULT now(),

    -- NOT NULL, which events.created_by deliberately is not. That column is
    -- nullable because every event that existed when 0020 landed was inserted
    -- by a person at a psql prompt and there was no honest id to record. There
    -- are no announcements predating this table, every one of them is written
    -- by a session holding announcements.post, so a null here would not be an
    -- unknown author — it would be a bug.
    --
    -- ON DELETE is absent, matching events.created_by and applications.forms:
    -- removing an organizer must not quietly rewrite the record of what they
    -- said to four hundred people.
    posted_by  uuid        NOT NULL REFERENCES identity.people (id),

    -- --------------------------------------------------------- taking it back
    -- Retraction rather than deletion, and rather than editing.
    --
    -- There is no body history and no updated_at here because a posted notice
    -- is never rewritten. People have already read it: the portal has no
    -- unread state and sends no notification, so a body edited in place is a
    -- fact that silently changed underneath everybody who acted on the old
    -- one. "Judging is at 2pm" quietly becoming "judging is at 4pm" leaves
    -- somebody sitting in the wrong room with no way to know they were told
    -- twice. Retracting and posting again leaves both, in order, with the
    -- correction at the top — which is what a person reading a feed can
    -- actually follow.
    --
    -- The row stays after retraction. It is the record of what the team told
    -- everybody at the time, and a DELETE would make "we never said that" and
    -- "we said that and took it back" the same database state.
    retracted_at timestamptz,
    retracted_by uuid      REFERENCES identity.people (id),

    -- Bounded, and bounded small. This is a corridor notice on a phone screen,
    -- and an unbounded text column on a write endpoint is also somewhere to
    -- put a megabyte. Enforced here as well as in C# for the reason 0020's
    -- slug rule is: this is exactly the check a hand-written INSERT during the
    -- event skips.
    --
    -- btrim so that a body of nothing but spaces is refused rather than stored
    -- and rendered as an empty row somebody has to scroll past.
    CONSTRAINT announcement_body_is_short
        CHECK (length(btrim(body)) BETWEEN 1 AND 500),

    -- Both halves of a retraction or neither. A retracted_at with no
    -- retracted_by reads as a row somebody fixed by hand, which is exactly
    -- what it must not be possible to confuse a real retraction with.
    CONSTRAINT retraction_names_somebody
        CHECK ((retracted_at IS NULL) = (retracted_by IS NULL))
);

-- The one query the portal makes, on the one weekend everything is slow.
--
-- Partial on retracted_at IS NULL because a retracted notice is invisible to
-- every applicant read, so it has no business taking up space in the index the
-- feed is served from. Descending on posted_at because the screen is
-- newest-first and there is no reason to sort in the query.
CREATE INDEX announcements_event_idx
    ON applications.announcements (event_id, posted_at DESC)
 WHERE retracted_at IS NULL;

-- ------------------------------------------------------- what records this ---
-- No audit trigger, and that is a decision rather than an omission.
--
-- audit.entries is deliberately narrow: 0010 wires triggers onto the four
-- identity tables that decide who may do what, and its allowlist is the point
-- of it. An announcement changes nobody's access. The convention for a content
-- write that matters — applications.forms, applications.events — is a
-- created_by column and a named log event, and that is what this table has:
-- posted_by and retracted_by name the person inside the same statement that
-- made the change, and announcement.posted / announcement.retracted are the
-- lines an alert can fire on.
