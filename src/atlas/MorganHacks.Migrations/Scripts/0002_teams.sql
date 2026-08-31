-- Team baselines.
--
-- Taken from the draft table in morganhacks-registration-and-rbac.md, which
-- the team still has to ratify. Changing a baseline later is an UPDATE here,
-- not a code change, which is the point of checking permissions rather than
-- team names.
--
-- Permission strings must match the Permission type in MorganHacks.Identity.
-- Nothing in SQL enforces that: a check constraint would mean a migration
-- every time a permission is added, and TryParse already ignores anything the
-- code does not recognise.

INSERT INTO identity.teams (slug, name) VALUES
    ('super-admin',  'Super admin'),
    ('registration', 'Registration'),
    ('comms',        'Comms'),
    ('sponsorship',  'Sponsorship'),
    ('logistics',    'Logistics'),
    ('judge',        'Judge'),
    ('volunteer',    'Volunteer');

-- Super admin: everything, including the two nobody else gets.
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, p FROM identity.teams, unnest(ARRAY[
    'applications.view','applications.decide','applications.bulk_decide',
    'applications.export','applications.view_resume','applications.note',
    'email.send_templated','email.send_broadcast','email.manage_templates','email.view_stats',
    'sponsors.view','sponsors.edit','sponsors.view_financials',
    'checkin.scan','swag.scan','checkin.view_stats',
    'judging.score_assigned','judging.view_all','judging.assign',
    'people.view','people.manage_teams','people.grant_permissions','audit.view'
]) AS p WHERE slug = 'super-admin';

INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, p FROM identity.teams, unnest(ARRAY[
    'applications.view','applications.decide','applications.bulk_decide',
    'applications.view_resume','applications.note',
    'email.send_templated'
]) AS p WHERE slug = 'registration';

-- Comms can see applications to build segments, but cannot decide them.
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, p FROM identity.teams, unnest(ARRAY[
    'email.send_broadcast','email.send_templated','email.manage_templates','email.view_stats',
    'applications.view'
]) AS p WHERE slug = 'comms';

INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, p FROM identity.teams, unnest(ARRAY[
    'sponsors.view','sponsors.edit','email.send_templated'
]) AS p WHERE slug = 'sponsorship';

-- Logistics needs headcount and dietary needs, so applications.view. Not
-- view_resume: a resume is more sensitive than the rest of the record.
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, p FROM identity.teams, unnest(ARRAY[
    'applications.view','checkin.scan','swag.scan'
]) AS p WHERE slug = 'logistics';

-- Judges see assigned projects and nothing else. Their membership carries an
-- expiry so access dies the day after the event rather than when somebody
-- remembers.
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, 'judging.score_assigned' FROM identity.teams WHERE slug = 'judge';

-- Volunteers get nothing that reads PII in bulk.
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, p FROM identity.teams, unnest(ARRAY[
    'checkin.scan','swag.scan'
]) AS p WHERE slug = 'volunteer';
