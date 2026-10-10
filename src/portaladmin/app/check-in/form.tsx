"use client";

import Link from "next/link";
import { useActionState, useEffect, useRef } from "react";
import { scanCheckIn } from "./actions";

/**
 * The volunteer's view: one input, one answer.
 *
 * Built for a queue — the input keeps focus so the next code can be
 * typed the moment the last answer lands, and the answer stays on
 * screen until the next scan replaces it. The name is the largest
 * thing in the answer: a code that never rotates is useless
 * forwarded only because somebody reads this while looking at
 * whoever handed over the phone.
 */
export function ScanForm() {
  const [state, action, pending] = useActionState(scanCheckIn, {});
  const input = useRef<HTMLInputElement>(null);

  // Back to the input after every answer. A desk that makes the
  // volunteer click before each of four hundred scans is broken.
  useEffect(() => {
    input.current?.focus();
    input.current?.select();
  }, [state]);

  const ok = state.outcome === "checkedIn" || state.outcome === "alreadyCheckedIn";
  const refused = state.outcome === "notConfirmed" || state.outcome === "unknownCode";

  return (
    <div>
      <form action={action}>
        <div>
          <label htmlFor="checkin-code">Check-in code</label>
          <input
            ref={input}
            id="checkin-code"
            name="code"
            required
            autoComplete="off"
            autoFocus
            spellCheck={false}
            autoCapitalize="characters"
            placeholder="e.g. K7QM 4XPT 9BD2"
            disabled={pending}
            style={{ textTransform: "uppercase" }}
          />
        </div>

        <div style={{ marginTop: 12 }}>
          <button type="submit" className="button primary" disabled={pending}>
            {pending ? "Checking…" : "Check in"}
          </button>
        </div>
      </form>

      {/* The answer, in the API's own words. */}
      {state.message && ok ? (
        <div
          role="status"
          aria-live="polite"
          style={{
            marginTop: 16,
            padding: 12,
            border: "1px solid",
            borderRadius: 8,
          }}
        >
          <p style={{ margin: 0, fontWeight: 600 }}>{state.message}</p>
          {state.name ? (
            <p style={{ margin: "8px 0 0", fontSize: 20 }}>{state.name}</p>
          ) : null}
          {state.outcome === "alreadyCheckedIn" && state.checkedInAt ? (
            <p style={{ margin: "4px 0 0" }}>
              Already in since {new Date(state.checkedInAt).toLocaleString()}. Let them through.
            </p>
          ) : null}
        </div>
      ) : null}

      {state.error && refused ? (
        <div
          role="alert"
          style={{
            marginTop: 16,
            padding: 12,
            border: "1px solid",
            borderRadius: 8,
          }}
        >
          <p style={{ margin: 0, fontWeight: 600 }}>{state.error}</p>
          {state.name ? (
            <p style={{ margin: "8px 0 0", fontSize: 20 }}>{state.name}</p>
          ) : null}
          {state.outcome === "notConfirmed" ? (
            <p style={{ margin: "8px 0 0" }}>Send them to an organizer.</p>
          ) : null}
        </div>
      ) : null}

      {state.error && !refused ? (
        <p role="alert" style={{ marginTop: 12 }}>
          {state.error}
        </p>
      ) : null}

      <p style={{ marginTop: 24 }}>
        No code?{" "}
        <Link href="/applicants">Look them up in Applicants</Link> instead — a
        dead phone is not a refusal.
      </p>
    </div>
  );
}
