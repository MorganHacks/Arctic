"use client";

import { useState, useTransition } from "react";
import type {
  CancelResult,
  PreviewResult,
  SendResult,
} from "@/app/mail/actions";
import styles from "./mail.module.css";
import { StatusPill } from "./status";
import { when, type Campaign, type CampaignStatus, type Preview } from "./types";

/**
 * The preview, the send, and the cancel.
 *
 * The order is the point. A draft offers one button, and it resolves the
 * recipients; the send appears only once somebody has the count and a sample
 * of the addresses in front of them. Somebody is about to mail several hundred
 * people and cannot take it back, so the screen refuses to let that happen
 * with a number nobody has looked at.
 *
 * The disabled button is a courtesy, not the control: the send action resolves
 * the recipients again on the server and refuses if the count has moved since
 * the preview. That covers the person who previewed, went for coffee, and came
 * back to a segment forty people larger.
 */
export function Sending({
  campaign,
  canSend,
  preview,
  send,
  cancel,
}: {
  campaign: Campaign;
  canSend: boolean;
  preview: () => Promise<PreviewResult>;
  send: (seen: number) => Promise<SendResult>;
  cancel: () => Promise<CancelResult>;
}) {
  const [resolved, setResolved] = useState<Preview | null>(null);
  const [asked, setAsked] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  /*
   * What this screen has done itself, on top of what it was rendered with.
   *
   * The server revalidates and the page comes back in its new state, but until
   * it does the send button would still be on screen and pressable. Holding
   * the outcome locally means the second press cannot happen at all.
   */
  const [became, setBecame] = useState<CampaignStatus | null>(null);
  const status = became ?? campaign.status;

  function runPreview() {
    setError(null);
    startTransition(async () => {
      const result = await preview();

      if (result.ok) {
        setResolved(result.preview);
        setAsked(false);
      } else {
        setError(result.error);
      }
    });
  }

  function runSend() {
    if (!resolved) {
      return;
    }

    setError(null);
    startTransition(async () => {
      const result = await send(resolved.recipientCount);
      setAsked(false);

      if (result.ok) {
        setBecame("queued");
        setResolved({ ...resolved, recipientCount: result.recipientCount });
      } else {
        setError(result.error);
        // The recipients moved. Showing the new set is the only thing that
        // makes "check them again" a sentence somebody can act on.
        if (result.preview) {
          setResolved(result.preview);
        }
      }
    });
  }

  function runCancel() {
    setError(null);
    startTransition(async () => {
      const result = await cancel();
      setAsked(false);

      if (result.ok) {
        setBecame("cancelled");
      } else {
        setError(result.error);
      }
    });
  }

  if (status !== "draft") {
    const count = resolved?.recipientCount ?? campaign.recipientCount;

    return (
      <section className="panel">
        <h2>Sending</h2>

        <div className={`${styles.outcome} ${toneOf(status)}`}>
          <StatusPill status={status} />
          <p className={styles.headline}>{count}</p>
          <p>{people(count)}</p>
          <p>{outcomeLine(status, campaign.sentAt)}</p>
        </div>

        {status === "queued" && canSend ? (
          asked ? (
            <div className={styles.confirm}>
              <span>Cancel this campaign?</span>
              <button
                type="button"
                className="danger"
                onClick={runCancel}
                disabled={pending}
              >
                {pending ? "Cancelling…" : "Yes, cancel it"}
              </button>
              <button type="button" onClick={() => setAsked(false)}>
                Keep it
              </button>
            </div>
          ) : (
            <div className={styles.actions}>
              <button
                type="button"
                className="danger"
                onClick={() => setAsked(true)}
              >
                Cancel campaign
              </button>
            </div>
          )
        ) : null}

        {error ? <p className="error">{error}</p> : null}
      </section>
    );
  }

  return (
    <section className="panel">
      <h2>Recipients</h2>

      {resolved === null ? (
        <>
          <p className="meta">
            Nothing can be sent until the recipients have been resolved.
          </p>

          <div className={styles.actions}>
            <button type="button" onClick={runPreview} disabled={pending}>
              {pending ? "Resolving…" : "Preview recipients"}
            </button>
          </div>
        </>
      ) : (
        <>
          <p className={styles.headline}>{resolved.recipientCount}</p>
          <p className="meta">{people(resolved.recipientCount)}</p>

          {resolved.sample.length > 0 ? (
            <>
              <ul className={styles.sample}>
                {resolved.sample.map((address) => (
                  <li key={address}>{address}</li>
                ))}
              </ul>
              <p className="count">
                {resolved.sample.length} of {resolved.recipientCount} shown
              </p>
            </>
          ) : null}

          {asked ? (
            <div className={styles.confirm}>
              <span>
                Send to {resolved.recipientCount}{" "}
                {people(resolved.recipientCount)}? This cannot be undone.
              </span>
              <button
                type="button"
                className="danger"
                onClick={runSend}
                disabled={pending}
              >
                {pending ? "Sending…" : "Yes, send it"}
              </button>
              <button type="button" onClick={() => setAsked(false)}>
                Back
              </button>
            </div>
          ) : (
            <div className={styles.actions}>
              <button type="button" onClick={runPreview} disabled={pending}>
                {pending ? "Resolving…" : "Preview again"}
              </button>

              {canSend && resolved.recipientCount > 0 ? (
                <button
                  type="button"
                  className="danger"
                  onClick={() => setAsked(true)}
                  disabled={pending}
                >
                  Send to {resolved.recipientCount}{" "}
                  {people(resolved.recipientCount)}
                </button>
              ) : null}
            </div>
          )}

          {resolved.recipientCount === 0 ? (
            <p className="meta">Nobody matches this segment.</p>
          ) : null}

          {/* Cosmetic. The API refuses the send whether or not this button
              rendered; hiding it is a courtesy to somebody who cannot use it. */}
          {canSend ? null : (
            <p className="meta">
              You do not have email.send_broadcast. Ask an admin.
            </p>
          )}
        </>
      )}

      {error ? <p className="error">{error}</p> : null}
    </section>
  );
}

/** "recipient" or "recipients", so the confirmation reads as a sentence. */
function people(count: number): string {
  return count === 1 ? "recipient" : "recipients";
}

/**
 * The edge colour on a campaign that is no longer a draft.
 *
 * Three states, three meanings: it went, it is going, or it did not. Nothing
 * else on the page is coloured, so these read.
 */
function toneOf(status: CampaignStatus): string {
  if (status === "sent") {
    return styles.done;
  }

  if (status === "queued" || status === "sending") {
    return styles.running;
  }

  return styles.stopped;
}

function outcomeLine(status: CampaignStatus, sentAt: string | null): string {
  if (status === "sent") {
    return sentAt === null ? "Sent." : `Sent ${when(sentAt)}.`;
  }

  if (status === "queued") {
    return "Queued.";
  }

  if (status === "sending") {
    return "Sending now.";
  }

  if (status === "cancelled") {
    return "Cancelled.";
  }

  if (status === "failed") {
    return "Sending failed.";
  }

  return "";
}
