-- Broadcast sends from mail@morganhacks.com.
--
-- 0022 moved broadcast off the apex and onto mail.morganhacks.com, which was
-- right about the IAM refusal and produced an address nobody wants to read:
-- from_local 'mail' on from_domain 'mail.morganhacks.com' is mail@mail. The
-- doubled word is the whole reason this exists.
--
-- The reputation split that 0003 cares about is between transactional and
-- broadcast, and it survives: sign-in mail stays on auth.morganhacks.com and is
-- not touched here. What changes is which domain carries the announcements, and
-- it is now the apex. That is a real trade and it was made deliberately -- a
-- burnt subdomain can be abandoned and the apex cannot -- against an address
-- people will actually see on every email the event sends.
--
-- The apex is verified in SES us-east-2 with its own DKIM keys, and its custom
-- MAIL FROM is bounce.morganhacks.com so bounces do not land on the apex MX,
-- which belongs to Cloudflare Email Routing and is nothing to do with us.

UPDATE notify.templates
   SET from_domain = 'morganhacks.com'
 WHERE kind = 'broadcast'
   AND from_domain <> 'morganhacks.com';

-- Transactional is deliberately not in that statement. auth.morganhacks.com is
-- correct, is separately verified, and a WHERE clause that caught it would put
-- every sign-in link behind the reputation of the announcements.
