-- Templates written in HTML as well as in Markdown.
--
-- 0017 gave a template a source column on the assumption that the source was
-- always Markdown, and the sanitiser behind it threw away everything Markdown
-- could not produce. That assumption was wrong about email rather than wrong
-- about safety: HTML is what mail is written in, inline styles are how every
-- client is made to agree on a colour, and a table is still the only layout
-- primitive Outlook renders reliably — so a body with no tables and no style
-- attribute cannot contain a button, which is most of what an event mails
-- people.
--
-- What that costs is one column. body_markdown already holds the text an
-- author typed and body_html already holds what will be sent; the only thing
-- missing is which language the first of those is in, because the two are read
-- back into an editor and rendered by different code.

-- ------------------------------------------------------------- the dialect ---
-- Which language body_markdown is written in.
--
-- NOT NULL with a default, so every row that exists keeps meaning exactly what
-- it meant: a Markdown template whose HTML was generated from its source. Only
-- rows written after this migration can say 'html', and they say it because an
-- author chose it.
--
-- Not fixed across versions, unlike kind. Converting a template from Markdown
-- to HTML is a thing an author legitimately does once the layout outgrows the
-- dialect, and it changes nothing about which queue the message joins or which
-- subdomain it leaves from — which is the whole reason kind cannot move.
ALTER TABLE notify.templates
    ADD COLUMN body_format text NOT NULL DEFAULT 'markdown'
        CHECK (body_format IN ('markdown', 'html'));

-- The name is now half true, and renaming it is worse than leaving it.
--
-- Migrations run as a pre-deploy job, so a rename lands while the previous
-- version of the API is still serving: every template read would fail for the
-- length of the deploy. The column holds the source in whatever body_format
-- says, and this comment is where somebody reading the table finds that out.
COMMENT ON COLUMN notify.templates.body_markdown IS
    'The source an author typed, in the language body_format names. Called '
    'body_markdown because Markdown was the only language when 0017 added it.';

COMMENT ON COLUMN notify.templates.body_format IS
    'markdown or html — how to read body_markdown, and which renderer '
    'regenerates body_html and body_text from it.';

-- ---------------------------------------------------------- the seeded row ---
-- magic_link was written as HTML by hand in 0005 and has no source at all:
-- body_markdown is null on it. It stays 'markdown' rather than becoming 'html'
-- for a reason that is not pedantry — body_format describes body_markdown, and
-- there is nothing there to describe. Claiming 'html' would tell the editor to
-- open an HTML document that does not exist. The first save through the
-- console gives that row both a source and a format that means something.
