INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, 'email.delete_templates'
FROM identity.teams
WHERE slug IN ('registration', 'comms', 'super-admin')
ON CONFLICT DO NOTHING;
