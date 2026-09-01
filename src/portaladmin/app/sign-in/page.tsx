/**
 * The only unauthenticated screen.
 *
 * Google, and no password field. Organizer access is tied to an allowlisted
 * account and a Google subject bound on first sign-in, so there is nothing here
 * for a password to be checked against — and the link goes through this app's
 * own origin, so the whole round trip stays on one hostname.
 */
export default async function SignIn({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  const { error } = await searchParams;

  return (
    <div className="signin">
      <div className="card">
        <h1>MorganHacks console</h1>
        <p>For organizers.</p>

        {error ? (
          <p className="error">
            That account is not set up as an organizer. Ask an admin to add you.
          </p>
        ) : null}

        <a className="button primary" href="/api/auth/google">
          Continue with Google
        </a>

        <p className="note">
          Use the Google account an admin added for you. Applicants sign in
          somewhere else.
        </p>
      </div>
    </div>
  );
}
