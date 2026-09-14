-- The note somebody gets when they first become a working organizer.
--
-- Seeded like the sign-in template, and for the same reason: atlas queues
-- against this key, and an environment without the row logs loudly and drops
-- the send. The fix for that is this row existing everywhere.
--
-- WHY IT SENDS ON A FIRST TEAM RATHER THAN ON BEING ADDED
--
-- Being on the allowlist grants nothing. An organizer with no teams signs in
-- to a console that shows them almost nothing, so an email sent at that moment
-- would be telling the truth in a way that reads as a broken account. Joining
-- a first team is the moment access becomes real, and it happens exactly once
-- per person, so nobody is mailed twice by joining a second one.
--
-- WHAT THE COPY IS FOR
--
-- Not politeness. The failure this prevents is somebody signing in with the
-- wrong Google account — added as name@morgan.edu, trying a personal Gmail,
-- and getting a refusal that cannot explain itself without telling strangers
-- which addresses are on the allowlist. This email arrives in the inbox that
-- works, so "use the account this was sent to" is unambiguous.
--
-- COPY: the wording below needs signing off before any real send.
--
-- The from address stays on auth.morganhacks.com with the sign-in link. It is
-- transactional, it is about getting into an account, and the whole point of
-- the subdomain split is that a broadcast collecting spam complaints must not
-- take this kind of mail down with it. Both the wording and the address change
-- with an UPDATE against this row, not a deployment.

INSERT INTO notify.templates
    (key, kind, subject, body_html, body_text, from_local, from_domain, reply_to)
VALUES (
    'organizer_welcome',
    'transactional',
    'You have access to the MorganHacks organizer console',

    '<p>You have been added to the MorganHacks organizer team.</p>'
    '<p><a href="{{console}}">Open the console</a></p>'
    '<p>Sign in with Google using <strong>{{email}}</strong>. That is the '
    'address you were added with, and it is the only one that will let you in '
    '&mdash; a different Google account will be turned away.</p>'
    '<p>What you can see depends on the teams you are on, so it may be a '
    'short list to start with.</p>'
    '<p>If you were not expecting this, reply and tell us.</p>',

    E'You have been added to the MorganHacks organizer team.\n\n{{console}}\n\nSign in with Google using {{email}}. That is the address you were added with, and it is the only one that will let you in - a different Google account will be turned away.\n\nWhat you can see depends on the teams you are on, so it may be a short list to start with.\n\nIf you were not expecting this, reply and tell us.',

    'login',
    'auth.morganhacks.com',
    'hello@morganhacks.com'
);
