"use client";

import { useState, useTransition, type ReactNode } from "react";
import type {
  CancelResult,
  PreviewResult,
  SendResult,
} from "@/app/mail/actions";
import styles from "./mail.module.css";
import { StatusPill } from "./status";
import {
  when,
  type Campaign,
  type CampaignStatus,
  type MessageProgress,
  type Preview,
} from "./types";

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
 *
 * The two stages are numbered on screen rather than merely implied. A screen
 * that shows a send control and a preview control together reads as two ways
 * of doing one thing; numbering them says they are one thing done in order,
 * and that the second cannot happen without the first.
 *
 * This component owns the page's layout below the title because the panel
 * describing the campaign has to sit beside the panel resolving it, and only
 * one of the two knows whether the send region exists yet. The description is
 * handed in as `facts` and rendered untouched.
 */
export function Sending({
  campaign,
  canSend,
  me,
  messages,
  facts,
  preview,
  send,
  cancel,
}: {
  campaign: Campaign;
  canSend: boolean;

  /** Who is reading, so the two names on the campaign can be read as names. */
  me: string;

  /** What happened to the messages, once there are any. Null on a draft. */
  messages: MessageProgress | null;
  facts: ReactNode;
  preview: () => Promise<PreviewResult>;
  send: (seen: number) => Promise<SendResult>;
  cancel: () => Promise<CancelResult>;
}) {
  const [resolved, setResolved] = useState<Preview | null>(null);
  const [asked, setAsked] = useState(false);
  const [error, setError] = useState<string | null>(null);

  /*
   * A refusal, as opposed to something that went wrong with the count.
   *
   * Kept apart because they belong in different places. "The recipients
   * changed since you previewed" is a fact about the number and belongs beside
   * it; a refusal is a fact about whether this person may send at all, and
   * belongs at the top with the other thing said about who may send. The
   * action tells them apart by handing back a new preview with the first and
   * not with the second.
   */
  const [refused, setRefused] = useState<string | null>(null);
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
    setRefused(null);
    startTransition(async () => {
      const result = await send(resolved.recipientCount);
      setAsked(false);

      if (result.ok) {
        setBecame("queued");
        setResolved({ ...resolved, recipientCount: result.recipientCount });
        return;
      }

      // The recipients moved. Showing the new set is the only thing that
      // makes "check them again" a sentence somebody can act on.
      if (result.preview) {
        setResolved(result.preview);
        setError(result.error);
        return;
      }

      // Everything else is the API declining, in its own words. Two-person
      // approval is the one that will be met most often, and it is put where
      // the approval rule is already being explained rather than under the
      // button, because the answer to it is another person rather than another
      // press.
      setRefused(result.error);
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

  return (
    <>
      <Approval campaign={campaign} me={me} refused={refused} />

      <div className={styles.layout}>
        {facts}

        {status !== "draft" ? (
          <Outcome
            campaign={campaign}
            status={status}
            count={resolved?.recipientCount ?? campaign.recipientCount}
            messages={messages}
            canSend={canSend}
            asked={asked}
            pending={pending}
            error={error}
            ask={() => setAsked(true)}
            unask={() => setAsked(false)}
            confirm={runCancel}
          />
        ) : (
          <>
            <section className={styles.card}>
              <div className={styles.cardHead}>
                <h2>
                  <span className={styles.stage}>Stage 1</span>{" "}
                  <span className={styles.sep}>·</span> Recipients
                </h2>
              </div>

              <div className={styles.cardBody}>
                {resolved === null ? (
                  <>
                    <p className="meta" style={{ margin: 0 }}>
                      Nothing can be sent until the recipients have been
                      resolved.
                    </p>

                    <div className={styles.actions}>
                      <button
                        type="button"
                        onClick={runPreview}
                        disabled={pending}
                      >
                        {pending ? "Resolving…" : "Preview recipients"}
                      </button>
                    </div>
                  </>
                ) : (
                  <Resolved preview={resolved} />
                )}

                {resolved !== null ? (
                  <div className={styles.actions}>
                    <button
                      type="button"
                      onClick={runPreview}
                      disabled={pending}
                    >
                      {pending ? "Resolving…" : "Preview again"}
                    </button>
                  </div>
                ) : null}

                {resolved !== null && resolved.recipientCount === 0 ? (
                  <p className="meta">Nobody matches this segment.</p>
                ) : null}

                {/* Cosmetic. The API refuses the send whether or not this
                    button rendered; hiding it is a courtesy to somebody who
                    cannot use it. */}
                {resolved !== null && !canSend ? (
                  <p className="meta">
                    You do not have email.send_broadcast. Ask an admin.
                  </p>
                ) : null}

                {error ? <p className="error">{error}</p> : null}
              </div>
            </section>

            {/*
              The send exists only here, and only once there is a resolved
              count on the screen above it. Not disabled beforehand — absent,
              because a greyed-out button still tells somebody that pressing
              is the shape of what they are doing, and the shape of this is
              "look first".
            */}
            {resolved !== null && canSend && resolved.recipientCount > 0 ? (
              <section
                className={`${styles.commit} ${styles.full} ${styles.revealed}`}
              >
                <p className={styles.commitHead}>
                  <span className={styles.stage}>Stage 2</span>
                </p>

                {asked ? (
                  <div className={styles.confirm}>
                    <span className={styles.confirmAsk}>
                      Send to {resolved.recipientCount}{" "}
                      {people(resolved.recipientCount)}? This cannot be undone.
                    </span>

                    <div className={styles.actions}>
                      <button
                        type="button"
                        className={styles.send}
                        onClick={runSend}
                        disabled={pending}
                      >
                        {pending ? "Sending…" : "Yes, send it"}
                      </button>
                      <button type="button" onClick={() => setAsked(false)}>
                        Back
                      </button>
                    </div>
                  </div>
                ) : (
                  <div className={styles.actions} style={{ marginTop: 0 }}>
                    <button
                      type="button"
                      className={styles.send}
                      onClick={() => setAsked(true)}
                      disabled={pending}
                    >
                      Send to {resolved.recipientCount}{" "}
                      {people(resolved.recipientCount)}
                    </button>
                  </div>
                )}
              </section>
            ) : null}
          </>
        )}
      </div>
    </>
  );
}

/**
 * The two names on a campaign, and the API's answer when there is only one.
 *
 * Rendered before anything else because it decides whether the rest of the
 * screen leads anywhere for this reader. The sentence is never written here:
 * a refusal arrives from the API and is shown exactly as it arrived, which is
 * the only way the wording on the screen and the wording in the log stay the
 * same sentence.
 */
function Approval({
  campaign,
  me,
  refused,
}: {
  campaign: Campaign;
  me: string;
  refused: string | null;
}) {
  const author = campaign.createdBy ?? null;
  const approver = campaign.approvedBy ?? null;

  if (author === null && approver === null && refused === null) {
    return null;
  }

  return (
    <section
      className={`${styles.approval} ${refused ? styles.blocked : ""}`.trim()}
    >
      {refused ? <p className={styles.approvalSaid}>{refused}</p> : null}

      <div className={styles.approvalWho}>
        {author ? (
          <span>
            Created by <code>{shortId(author)}</code>
            {author === me ? " (you)" : ""}
          </span>
        ) : null}

        {approver ? (
          <span>
            Approved by <code>{shortId(approver)}</code>
            {approver === me ? " (you)" : ""}
          </span>
        ) : null}
      </div>
    </section>
  );
}

/**
 * Who a send would reach, now.
 *
 * Three numbers rather than one. "412 matched, 400 will be sent, 12
 * suppressed" is the sentence somebody needs, because the twelve are recorded
 * rather than discarded and are meant to be findable — and because a bare 400
 * hides the fact that anybody was held back at all.
 */
function Resolved({ preview }: { preview: Preview }) {
  const matched = preview.segmentSize ?? preview.recipientCount;
  const held = preview.suppressedCount ?? matched - preview.recipientCount;

  const reasons = Object.entries(preview.suppressedByReason ?? {}).filter(
    ([, count]) => count > 0,
  );

  const problems = preview.problems ?? [];

  return (
    <div className={styles.revealed}>
      <div className={styles.tiles}>
        <dl className={styles.tile}>
          <dt>Matched</dt>
          <dd>
            <b className={styles.headline}>{matched}</b>
          </dd>
        </dl>

        <dl className={`${styles.tile} ${styles.sendable}`}>
          <dt>Will be sent</dt>
          <dd>
            <b className={styles.headline}>{preview.recipientCount}</b>
          </dd>
        </dl>

        <dl className={`${styles.tile} ${styles.held}`}>
          <dt>Suppressed</dt>
          <dd>
            <b className={styles.headline}>{held}</b>
          </dd>
        </dl>
      </div>

      {reasons.length > 0 ? (
        <ul className={styles.reasons}>
          {reasons.map(([reason, count]) => (
            <li key={reason}>
              <span className={styles.reasonName}>{reason}</span>
              <span className={styles.reasonCount}>{count}</span>
            </li>
          ))}
        </ul>
      ) : null}

      {problems.length > 0 ? (
        <ul className={styles.problems}>
          {problems.map((problem) => (
            <li key={problem}>{problem}</li>
          ))}
        </ul>
      ) : null}

      {preview.sample.length > 0 ? (
        <>
          <ul className={styles.sample}>
            {preview.sample.map((address) => (
              <li key={address}>{address}</li>
            ))}
          </ul>
          <p className={`count ${styles.sampleFoot}`}>
            {preview.sample.length} of {preview.recipientCount} shown
          </p>
        </>
      ) : null}
    </div>
  );
}

/**
 * A campaign that is no longer a draft.
 *
 * The send controls are gone rather than greyed out — there is nothing left to
 * offer, and a disabled Send beside a campaign that has already gone is an
 * invitation to wonder whether it went. Cancel takes its place while there is
 * still something in the queue to stop.
 */
function Outcome({
  campaign,
  status,
  count,
  messages,
  canSend,
  asked,
  pending,
  error,
  ask,
  unask,
  confirm,
}: {
  campaign: Campaign;
  status: CampaignStatus;
  count: number;
  messages: MessageProgress | null;
  canSend: boolean;
  asked: boolean;
  pending: boolean;
  error: string | null;
  ask: () => void;
  unask: () => void;
  confirm: () => void;
}) {
  return (
    <section className={styles.card}>
      <div className={styles.cardHead}>
        <h2>Sending</h2>
        <StatusPill status={status} />
      </div>

      <div className={styles.cardBody}>
        <div className={`${styles.outcome} ${toneOf(status)}`}>
          <b className={styles.headline}>{count}</b>
          <p>{people(count)}</p>
          <p>{outcomeLine(status, campaign.sentAt)}</p>
        </div>

        {messages && messages.total > 0 ? (
          <Progress messages={messages} />
        ) : null}

        {status === "queued" && canSend ? (
          asked ? (
            <div className={styles.confirm}>
              <span className={styles.confirmAsk}>Cancel this campaign?</span>

              <div className={styles.actions}>
                <button
                  type="button"
                  className="danger"
                  onClick={confirm}
                  disabled={pending}
                >
                  {pending ? "Cancelling…" : "Yes, cancel it"}
                </button>
                <button type="button" onClick={unask}>
                  Keep it
                </button>
              </div>
            </div>
          ) : (
            <div className={styles.actions}>
              <button type="button" className="danger" onClick={ask}>
                Cancel campaign
              </button>
            </div>
          )
        ) : null}

        {error ? <p className="error">{error}</p> : null}
      </div>
    </section>
  );
}

/**
 * How far through the queue a campaign got, counted per message.
 *
 * Shown as the sender's own status words rather than translated into three
 * friendly ones. `bounced` and `failed_perm` are different things that need
 * different work afterwards, and a screen that called both of them "not
 * delivered" would be hiding which.
 */
function Progress({ messages }: { messages: MessageProgress }) {
  const counted = Object.entries(messages.byStatus).filter(
    ([, count]) => count > 0,
  );

  return (
    <ul className={styles.progress}>
      {/* Not one of the statuses, and the reason it is listed beside them: a
          message that bounced has left this system as surely as one that was
          delivered, and cancelling reaches neither. */}
      <li>
        <b>{messages.gone}</b>
        <span>gone</span>
      </li>

      {counted.map(([state, count]) => (
        <li key={state}>
          <b>{count}</b>
          <span>{state}</span>
        </li>
      ))}
    </ul>
  );
}

/** The first eight characters, as the header shows them. */
function shortId(id: string): string {
  return id.slice(0, 8);
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
