"use client";

import { useState, useTransition } from "react";
import { postNotice, retractNotice } from "@/app/events/actions";
import type { AdminAnnouncement } from "@/app/events/announcements";
import styles from "./events.module.css";
import { readable } from "./zone";

/**
 * Notices posted to everybody at this event.
 *
 * Lives on the event rather than under its own nav item because that is what
 * an announcement belongs to: the endpoint is scoped to an event, the feed an
 * applicant reads is scoped to their event, and a top-level screen would have
 * to ask which one first — a question the URL has already answered.
 *
 * Retracted notices stay on this list, struck through. The applicant feed drops
 * them the moment they are retracted, so this screen is the only place the
 * difference between "never posted" and "posted and taken back" is visible,
 * and during an event that difference is the whole story of what people were
 * told.
 */
const LIMIT = 500;

export function Announcements({
  eventId,
  announcements,
  canPost,
}: {
  eventId: string;
  announcements: AdminAnnouncement[];
  canPost: boolean;
}) {
  const [body, setBody] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, start] = useTransition();

  const left = LIMIT - body.length;
  const tooLong = left < 0;
  const empty = body.trim() === "";

  function post() {
    setError(null);
    start(async () => {
      const result = await postNotice(eventId, body.trim());
      if (result.ok) {
        setBody("");
      } else {
        setError(result.error ?? null);
      }
    });
  }

  function retract(id: string) {
    setError(null);
    start(async () => {
      const result = await retractNotice(eventId, id);
      if (!result.ok) {
        setError(result.error ?? null);
      }
    });
  }

  return (
    <section className={styles.notice}>
      {/* COPY: everything visible in this component needs sign-off. */}
      <h2>Announcements</h2>
      <p className="lede">
        Posted to everybody with an application to this event, immediately.
        There is no draft and nobody is emailed.
      </p>

      {canPost ? (
        <div className={styles.noticeWrite}>
          <label htmlFor="notice">What has changed</label>
          <textarea
            id="notice"
            value={body}
            onChange={(event) => setBody(event.target.value)}
            rows={3}
            disabled={pending}
            placeholder="Judging is running half an hour late."
          />
          {/*
            Said before they type rather than after they submit. An announcement
            is read by every hacker at the event and cannot be edited once it is
            up, so the two facts that change what somebody writes belong above
            the button, not in an error underneath it.
          */}
          <p className={styles.noticeWarn}>
            Everyone at the event reads this, so keep one person&rsquo;s details
            out of it. It cannot be edited after posting &mdash; only taken down.
          </p>
          <div className={styles.noticeActions}>
            <span className={tooLong ? "error" : "meta"}>
              {tooLong ? `${-left} over` : `${left} left`}
            </span>
            <button
              type="button"
              className="primary"
              onClick={post}
              disabled={pending || empty || tooLong}
            >
              {pending ? "Posting…" : "Post"}
            </button>
          </div>
        </div>
      ) : (
        <p className="refusal">
          You do not have permission to post announcements. Ask an admin.
        </p>
      )}

      {error ? <p className="error">{error}</p> : null}

      {announcements.length === 0 ? (
        <div className="empty">Nothing has been posted for this event.</div>
      ) : (
        <ul className={styles.notices}>
          {announcements.map((notice) => (
            <li
              key={notice.id}
              className={notice.retracted ? styles.noticeGone : undefined}
            >
              <p className={styles.noticeBody}>{notice.body}</p>
              <p className="meta">
                {readable(notice.postedAt)}
                {notice.retracted
                  ? ` · taken down ${readable(notice.retractedAt)}`
                  : null}
              </p>
              {canPost && !notice.retracted ? (
                <button
                  type="button"
                  className={styles.noticeRetract}
                  onClick={() => retract(notice.id)}
                  disabled={pending}
                >
                  Take down
                </button>
              ) : null}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
