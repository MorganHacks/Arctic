"use client";

/**
 * The API being unreachable, or answering with something unexpected.
 *
 * Kept apart from "no such form" deliberately. Telling somebody with a
 * perfectly good link that their link is wrong sends them to find a different
 * one, which they will not find, and the actual problem — a service that is
 * down for a minute — goes unreported because the page blamed them for it.
 *
 * The underlying error is not shown. It would be a hostname and a status code,
 * which helps nobody standing in a hallway and tells a stranger where the API
 * lives.
 */
export default function Error({ reset }: { error: unknown; reset: () => void }) {
  return (
    <main className="notice">
      <h1>Something went wrong</h1>
      <p>We could not load this form just now. Your link is fine.</p>
      <button type="submit" onClick={reset}>
        Try again
      </button>
    </main>
  );
}
