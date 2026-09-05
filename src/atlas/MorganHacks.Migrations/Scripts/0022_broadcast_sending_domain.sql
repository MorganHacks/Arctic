-- Broadcast mail moves to mail.morganhacks.com.
--
-- The two-lane split in 0003 was always about reputation: transactional mail
-- sends from auth.morganhacks.com so that a complaint about an announcement
-- can never cost somebody their sign-in link. Broadcast templates were seeded
-- at morganhacks.com, which is the apex -- the domain the website answers on
-- and the one every human address belongs to. Marketing complaints landing
-- there put the whole domain's reputation behind an announcement.
--
-- It also did not send at all. The IAM user lark authenticates as may only
-- send when the From address matches a sending subdomain, so every broadcast
-- was refused with AccessDeniedException, logged as a warning, and dropped.
-- The campaign looked queued and nothing arrived.
--
-- This repoints existing broadcast templates. It deliberately does not touch
-- transactional rows: auth.morganhacks.com is correct and stays.

UPDATE notify.templates
   SET from_domain = 'mail.morganhacks.com'
 WHERE kind = 'broadcast'
   AND from_domain <> 'mail.morganhacks.com';

-- No check constraint here, though one was written and removed. Pinning the
-- domains in the schema reads as belt and braces, but it makes the table
-- refuse the addresses the test fixtures depend on -- news.example.invalid is
-- reserved precisely so a test can never send real mail, and a schema that
-- outlaws it forces every fixture onto a live domain. The guard belongs in
-- TemplateEndpoints, where a person is present to read the refusal and where
-- the domains can differ per environment.
