import { SignInForm } from "./form";

/**
 * The only screen an applicant can reach without a session.
 *
 * No password field, because no password exists: a link to the address you
 * applied with is the whole of hacker sign-in. Organizers sign in somewhere
 * else entirely, with Google.
 */
export default async function SignIn({
  searchParams,
}: {
  searchParams: Promise<{ link?: string }>;
}) {
  const { link } = await searchParams;

  return (
    <div className="signin">
      <div className="panel">
        <h1>Sign in</h1>
        <p className="quiet">
          Enter the email address you applied with and we will send you a link.
        </p>

        {/*
          One message for every kind of failed link. Expired, already used and
          never existed are not told apart here, for the same reason the API
          does not tell them apart: the difference only helps somebody working
          through tokens.
        */}
        {link ? (
          <div className="notice problem">
            <p>
              That link no longer works. Links last 15 minutes and can only be
              used once — ask for a new one below.
            </p>
          </div>
        ) : null}

        <SignInForm />
      </div>
    </div>
  );
}
