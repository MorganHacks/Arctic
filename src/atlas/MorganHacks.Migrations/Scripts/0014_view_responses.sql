-- Who may read the answers people gave.
--
-- Its own permission rather than applications.view, which is a much larger
-- group than the words suggest. Comms holds applications.view to build email
-- segments, logistics holds it for headcount and dietary needs, and the form
-- builder is behind it too — where seeing the questions is not seeing anybody's
-- answers to them. Reading several hundred essays about somebody's first
-- hackathon is a narrower thing than any of those.
--
-- Super admin and registration only. Registration is the team that actually
-- reads applications to decide them; everyone else who needs this gets an
-- individual grant, which is what grants are for. Adding it to a baseline
-- gives it to everyone on that team forever, and the person who needs it once
-- is rarely the reason to.
--
-- The CSV is not here. That is applications.export, which already exists,
-- already sits on the sensitive list, and already means "PII leaves the
-- system" — which is exactly what a spreadsheet on somebody's laptop is.

INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, 'applications.view_responses' FROM identity.teams
 WHERE slug IN ('super-admin', 'registration')
    ON CONFLICT DO NOTHING;
