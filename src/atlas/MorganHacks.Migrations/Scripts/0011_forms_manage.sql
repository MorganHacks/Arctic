-- Who may build the form.
--
-- Registration and super admin, and nobody else by default. Reading the queue
-- is a large group — comms and logistics both hold applications.view — but
-- deciding what several hundred people are asked is one team's job, and it is
-- a decision that cannot be corrected for anyone who has already answered.
--
-- Anybody else who needs it gets an individual grant, which is what grants are
-- for. Adding a permission to a baseline gives it to everyone on that team
-- forever, and the person who needs it this once is rarely the reason to.

INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, 'forms.manage' FROM identity.teams
 WHERE slug IN ('super-admin', 'registration')
    ON CONFLICT DO NOTHING;
