"use client";

import { useState, useTransition } from "react";
import { Megaphone01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { EmptyState } from "@/components/ui/empty-state";
import { postNotice, retractNotice } from "@/app/events/actions";
import type { AdminAnnouncement } from "@/app/events/announcements";
import { AnnouncementComposer } from "./announcement-composer";
import { AnnouncementAttachment } from "./announcement-attachment";
import { AudienceReactions } from "./announcement-reactions";
import { cleanAnnouncementContent, type AnnouncementContent } from "../../../../libs/ui/announcements";
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
  const [content, setContent] = useState<AnnouncementContent | null>(null);
  const [composerKey, setComposerKey] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [pending, start] = useTransition();

  function post(publishAt: string | null) {
    setError(null);
    start(async () => {
      const result = await postNotice(eventId, body.trim(), cleanAnnouncementContent(content), publishAt);
      if (result.ok) {
        setBody("");
        setContent(null);
        setComposerKey(key => key + 1);
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
    <section className={styles.notice} aria-labelledby="announcements-title">
      {/* COPY: everything visible in this component needs sign-off. */}
      <div className={styles.noticeHeading}>
        <Icon icon={Megaphone01Icon} size={20} /><h2 id="announcements-title">Announcements</h2>
      </div>
      <p className={styles.noticeDescription}>
        Share updates, polls and quizzes with everyone at this event.
        Post now or schedule for later. Nobody is emailed.
      </p>

      {canPost ? (
        <AnnouncementComposer key={composerKey} body={body} onBody={setBody} content={content}
          onContent={setContent} pending={pending} onPost={post} />
      ) : (
        <p className={styles.noticeDescription}>
          You do not have permission to post announcements. Ask an admin.
        </p>
      )}

      {error ? <p className={styles.error} role="alert">{error}</p> : null}

      {announcements.length === 0 ? canPost ? (
        <div className={styles.noticeEmpty}>
          <EmptyState title="No announcements yet" description="Nothing has been posted for this event." />
        </div>
      ) : null : (
        <ul className={styles.notices}>
          {announcements.map((notice) => (
            <li
              key={notice.id}
              className={notice.retracted ? styles.noticeGone : undefined}
            >
              <p className={styles.noticeBody}>{notice.body}</p>
              <AnnouncementAttachment content={notice.content} results={notice.results} />
              {!notice.scheduled ? <AudienceReactions reactions={notice.reactions} /> : null}
              <p className={styles.noticeMeta}>
                {notice.scheduled ? "Scheduled for " : ""}{readable(notice.publishAt ?? notice.postedAt)}
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
                  {notice.scheduled ? "Cancel scheduled post" : "Take down"}
                </button>
              ) : null}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
