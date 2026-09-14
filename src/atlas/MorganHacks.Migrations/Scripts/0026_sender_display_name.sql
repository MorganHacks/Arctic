-- The name a mail client shows instead of the address.
--
-- Without it, a client has nothing to display but the local part, so a
-- broadcast from mail@morganhacks.com arrives from somebody called "mail".
-- That is not only ugly: a sender name nobody recognises is one of the things
-- a person uses to decide an email is junk, and the mail we most need read is
-- the mail we send least often.
--
-- A column rather than a constant in the sending worker, for the same reason
-- the addresses are columns: this is copy. It changes when somebody decides
-- the team is called something else, and that decision should not need a
-- deployment.
--
-- Nullable on purpose. An empty name is a legitimate choice -- the address
-- alone -- and it is different from "nobody has set one yet" only in that
-- there is nothing to show either way. Every template that exists now gets
-- the same name, because they are all from the same people.

ALTER TABLE notify.templates ADD COLUMN from_name text;

UPDATE notify.templates SET from_name = 'MorganHacks';
