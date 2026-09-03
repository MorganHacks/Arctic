-- What a template needs before anybody can edit one.
--
-- 0003 built this table for a row that would be written once, by hand, in SQL,
-- and 0005 wrote the only one there has ever been. Everything below is what
-- changes when the row is written by a person in a browser instead: the source
-- they typed has to survive, and the version they sent has to stay readable
-- after they have edited it again.

-- ------------------------------------------------------------- the source ---
-- Authors write Markdown; body_html and body_text are both generated from it.
-- Without this column an edit is a round trip through generated HTML, which is
-- a template nobody edits twice.
--
-- Nullable, and it stays nullable. The magic_link row 0005 seeded was written
-- as HTML by hand and there is no honest Markdown source for it — inventing
-- one here would be inventing wording, which is not this file's to write. The
-- API hands back null for such a row and the first save through the editor
-- gives it a real source.
ALTER TABLE notify.templates ADD COLUMN body_markdown text;

-- --------------------------------------------------------- copy on write ---
-- Editing a template writes a NEW row and retires the old one, rather than
-- updating in place.
--
-- The reason is notify.campaigns.template_id. It is a foreign key to a
-- specific row, and campaigns are the record of what this event actually
-- mailed people. Updating in place would leave every sent campaign pointing at
-- a row whose wording has since changed — "what did we send on the 14th" would
-- answer with today's draft. The per-message columns in notify.messages hold
-- the literal bytes each recipient received, but a campaign that cannot say
-- which template it sent is a history with a hole in it.
--
-- The second reason is that it makes an existing safety check real.
-- CampaignEndpoints already refuses to send when the template a campaign was
-- approved against is no longer the row its key resolves to — "This campaign's
-- template has changed or been removed." Until now that could only fire if
-- somebody deleted a row. With copy on write it fires exactly when the wording
-- changed under an approved campaign, which is the case it was written for: an
-- approver signs off on a template and a segment together, and a broadcast
-- cannot be recalled.
--
-- Retired rows are never deleted. The foreign key would not allow it, and that
-- is the constraint doing its job rather than getting in the way.
ALTER TABLE notify.templates ADD COLUMN superseded_at timestamptz;

-- Who wrote this version. Nullable because the rows that predate the editor
-- have no author — a migration wrote them — and because a NOT NULL here would
-- mean no template could ever be created by the same hand-written SQL that
-- created the only one we have.
ALTER TABLE notify.templates ADD COLUMN created_by uuid REFERENCES identity.people (id);

-- The key is unique among live templates, not among all rows. A key with four
-- retired versions behind it is one template.
ALTER TABLE notify.templates DROP CONSTRAINT templates_key_key;

CREATE UNIQUE INDEX templates_live_key
    ON notify.templates (key) WHERE superseded_at IS NULL;

-- Two saves at once. The partial index above already makes the second INSERT
-- fail, and the endpoint retires the current row with a conditional UPDATE
-- first so the loser finds nothing to retire and is told to reload. This index
-- is the belt to that: version numbers a template has already used cannot come
-- back, whatever wrote them.
CREATE UNIQUE INDEX templates_key_version
    ON notify.templates (key, version);

-- ----------------------------------------------------------- kind is fixed ---
-- A template's kind cannot change, across versions or in place.
--
-- kind is not a label. EmailTemplate.Priority is derived from it, so it decides
-- which lane a message queues in, and from_domain beside it decides which
-- subdomain's reputation carries the message. Flipping magic_link to
-- 'broadcast' would put every sign-in link at priority 10, behind whatever
-- announcement is draining, sent from the domain that collects the spam
-- complaints — which is the precise failure the two-lane design in 0003 exists
-- to prevent. The other direction is no better: a broadcast template turned
-- transactional starts jumping the queue ahead of login mail.
--
-- Here rather than only in the endpoint because templates are still created by
-- hand-written SQL, and this is the one property of the row that an UPDATE
-- cannot be allowed to get wrong.
CREATE FUNCTION notify.template_kind_is_fixed() RETURNS trigger AS $$
DECLARE
    settled text;
BEGIN
    -- The row changing under itself. This is the shape the hand-written fix
    -- takes: one UPDATE against the one template there is.
    IF TG_OP = 'UPDATE' AND NEW.kind IS DISTINCT FROM OLD.kind THEN
        RAISE EXCEPTION
            'notify.templates.kind is fixed: % is %, not %',
            NEW.key, OLD.kind, NEW.kind;
    END IF;

    -- And a new version disagreeing with the versions behind it, which is the
    -- shape the editor could take if it stopped checking.
    SELECT kind INTO settled
      FROM notify.templates
     WHERE key = NEW.key AND id <> NEW.id
     LIMIT 1;

    IF settled IS NOT NULL AND settled <> NEW.kind THEN
        RAISE EXCEPTION
            'notify.templates.kind is fixed: % is already %, not %',
            NEW.key, settled, NEW.kind;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER templates_kind_is_fixed
    BEFORE INSERT OR UPDATE ON notify.templates
    FOR EACH ROW EXECUTE FUNCTION notify.template_kind_is_fixed();
