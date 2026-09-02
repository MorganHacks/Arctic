/**
 * A code that does not lead anywhere.
 *
 * Plain on purpose, and identical for every reason it can happen: a code
 * nobody issued, a code with a character mistyped, and a form whose only
 * version is still a draft. The API refuses to tell those apart — being able
 * to would be a way to find out which seven-character codes are real — and a
 * page that guessed would give away for free what the API is protecting.
 *
 * The fix it suggests is transcription, because that is what actually goes
 * wrong. These get read aloud at a club meeting and copied off a whiteboard.
 */
export function NoForm() {
  return (
    <main className="notice">
      <h1>No form here</h1>
      <p>
        Nothing is behind that link. Codes are seven letters and numbers, like{" "}
        <code>k3npqzr</code>.
      </p>
      <p>Check the one you were given and try again.</p>
    </main>
  );
}
