"use client";

import { useState } from "react";

/**
 * The email step of a form that is for people we already have on file.
 *
 * Two states and no third. Somebody puts in an address and is told a link is
 * on its way — and they are told exactly that whether or not we hold the
 * address, because the API answers identically either way and this page must
 * not be the thing that gives the difference away. "No account found" for one
 * address and "check your inbox" for another turns a link handed out on a
 * flyer into a way to ask who applied.
 *
 * There is deliberately no way back to the box from the confirmation. Anything
 * that reads as "try a different address" is an invitation to sit here trying
 * addresses, which is the same lookup service by hand.
 */
export function SignIn({ code, expired }: { code: string; expired: boolean }) {
  const [email, setEmail] = useState("");
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (sending) {
      return;
    }

    // The one check this page makes on its own, and it is about the box being
    // empty rather than about the address being real. Anything cleverer here
    // would be this page deciding which addresses exist.
    if (email.trim() === "") {
      setProblem("Enter your email address.");
      return;
    }

    setSending(true);
    setProblem(null);

    try {
      // Same origin, through the rewrite in next.config.ts, like the submit.
      const response = await fetch(
        `/api/forms/${encodeURIComponent(code)}/sign-in`,
        {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ email }),
        },
      );

      if (response.ok) {
        setSent(true);
        return;
      }

      const body = (await response.json().catch(() => ({}))) as { error?: string };
      setProblem(body.error ?? "That did not go through. Try again.");
    } catch {
      setProblem("We could not reach the server. Check your connection and try again.");
    } finally {
      setSending(false);
    }
  }

  if (sent) {
    return (
      <div className="summary" role="status">
        <p className="summary-lede">Check your email</p>
        <p>
          If that address is on file, a sign-in link is on its way. It expires in
          15 minutes and can only be used once.
        </p>
      </div>
    );
  }

  return (
    <form onSubmit={submit} noValidate aria-busy={sending || undefined}>
      {/* The link that did not work, said once and without saying which way it
          failed. Expired, already used and never issued are one message on
          purpose — telling them apart only helps somebody probing links. */}
      {expired ? (
        <div className="summary" role="alert">
          <p className="summary-lede">That link did not work.</p>
          <p>It may have expired or already been used. Ask for a new one below.</p>
        </div>
      ) : null}

      <div className={`question${problem ? " wrong" : ""}`}>
        <label className="prompt" htmlFor="sign-in-email">
          Your email address
        </label>
        <p className="help" id="sign-in-email-help">
          Use the address you gave us. We will email you a link that opens this
          form.
        </p>
        <input
          id="sign-in-email"
          name="email"
          type="email"
          autoComplete="email"
          inputMode="email"
          value={email}
          aria-describedby={
            problem ? "sign-in-email-help sign-in-email-problem" : "sign-in-email-help"
          }
          aria-invalid={problem ? true : undefined}
          onChange={(e) => {
            setEmail(e.target.value);
            setProblem(null);
          }}
        />
        {problem ? (
          <strong className="wrong-note" id="sign-in-email-problem">
            {problem}
          </strong>
        ) : null}
      </div>

      <div className="submit">
        <button type="submit" disabled={sending}>
          {sending ? "Sending…" : "Email me a link"}
        </button>
      </div>
    </form>
  );
}
