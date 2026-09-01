-- The one template login depends on.
--
-- Seeded rather than created by hand, because atlas queues against this key
-- and an environment without it cannot sign anybody in. A missing template is
-- logged loudly and drops the send rather than throwing, so the sign-in
-- endpoint keeps answering identically for known and unknown addresses — but
-- the fix for that is this row existing everywhere, not the error handling.
--
-- TWO THINGS HERE NEED SIGNING OFF BEFORE ANY REAL SEND:
--
--   1. The wording. It is deliberately plain and says only what the link does.
--   2. The from address. `auth.morganhacks.com` is a transactional subdomain
--      that must be created in DNS and verified in SES, with its own DKIM
--      records, before this can send.
--
-- Both change with an UPDATE against this row, not a code change.
--
-- The subdomain split is the point rather than decoration. Announcements and
-- login links sending from one domain means a blast that collects spam
-- complaints takes login deliverability down with it, and login is the one
-- kind of mail that must never stop arriving.

INSERT INTO notify.templates
    (key, kind, subject, body_html, body_text, from_local, from_domain, reply_to)
VALUES (
    'magic_link',
    'transactional',
    'Your MorganHacks sign-in link',

    '<p>Here is your sign-in link.</p>'
    '<p><a href="{{link}}">Sign in to MorganHacks</a></p>'
    '<p>It expires in 15 minutes and can only be used once.</p>'
    '<p>If you did not ask to sign in, you can ignore this email.</p>',

    E'Here is your sign-in link.\n\n{{link}}\n\nIt expires in 15 minutes and can only be used once.\n\nIf you did not ask to sign in, you can ignore this email.',

    'login',
    'auth.morganhacks.com',
    NULL
);
