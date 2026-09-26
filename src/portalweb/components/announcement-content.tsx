"use client";

import { useState, useTransition } from "react";
import { announcementLabels, videoEmbed, type AnnouncementContent, type AnnouncementResults } from "../../../libs/ui/announcements";
import styles from "./announcement-content.module.css";

export function AnnouncementAttachment({ id, content: initialContent, results: initialResults }: {
  id: string; content: AnnouncementContent | null; results: AnnouncementResults | null;
}) {
  const [content, setContent] = useState(initialContent);
  const [results, setResults] = useState(initialResults);
  const [selected, setSelected] = useState<number | null>(initialResults?.choice ?? null);
  const [error, setError] = useState<string | null>(null);
  const [unavailable, setUnavailable] = useState(false);
  const [pending, start] = useTransition();
  if (!content) return null;
  const answered = results?.choice != null;
  const quiz = content.kind === "quiz";
  const locked = unavailable || pending || (quiz && answered);
  const embed = content.kind === "video" ? videoEmbed(content.media?.[0]?.url ?? "") : null;
  function vote() {
    if (selected === null) return;
    setError(null);
    start(async () => {
      try {
        const response = await fetch(`/api/portal/announcements/${encodeURIComponent(id)}/vote`, {
          method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ choice: selected }),
        });
        const data = await response.json() as { error?: string; content?: AnnouncementContent; results?: AnnouncementResults };
        if (!response.ok || !data.content || !data.results) {
          if (response.status === 404 || response.status === 410) setUnavailable(true);
          setError(response.status === 401 ? "Your session has ended. Sign in again." : data.error || "Your response could not be saved. Try again.");
          return;
        }
        setContent(data.content);
        setResults(data.results);
        setSelected(data.results.choice);
      } catch { setError("Your response could not be saved. Check your connection and try again."); }
    });
  }
  return <div className={styles.attachment}>
    <p className={styles.kind}>{announcementLabels[content.kind]}</p>
    {content.kind === "image" ? <div className={styles.images}>{content.media?.map((item, index) => <a key={index} href={item.url} target="_blank" rel="noreferrer"><img src={item.url} alt={item.alt || "Announcement image"} loading="lazy" referrerPolicy="no-referrer" /></a>)}</div> : null}
    {content.kind === "video" && content.media?.[0] ? <div className={styles.video}>{embed ? <iframe src={embed} title="Announcement video" allowFullScreen loading="lazy" referrerPolicy="strict-origin-when-cross-origin" /> : <video src={content.media[0].url} controls preload="metadata" />}<a href={content.media[0].url} target="_blank" rel="noreferrer">Open video</a></div> : null}
    {content.options ? <>
      <fieldset className={styles.choices} disabled={locked}>
        <legend className={styles.legend}>{quiz ? "Choose your answer" : "Cast your vote"}</legend>
        {content.options.map((option, index) => {
          const count = results?.counts?.[index] ?? 0;
          const percent = results?.total ? Math.round(count / results.total * 100) : 0;
          return <label key={index} className={styles.choice} data-selected={selected === index} data-correct={quiz && answered && content.correctOption === index}>
            <input type="radio" name={`announcement-${id}`} value={index} checked={selected === index} onChange={() => setSelected(index)} />
            {option.imageUrl ? <img src={option.imageUrl} alt="" loading="lazy" referrerPolicy="no-referrer" /> : null}
            <span className={styles.answer}><span className={styles.answerLabel}><span>{option.text}{quiz && answered && content.correctOption === index ? <strong className={styles.correct}>Correct answer</strong> : null}</span>{results?.counts ? <span>{percent}%</span> : null}</span>{results?.counts ? <span className={styles.track}><span style={{ width: `${percent}%` }} /></span> : null}</span>
          </label>;
        })}
      </fieldset>
      <div className={styles.actions}>
        <span>{results?.total ?? 0} {quiz ? results?.total === 1 ? "answer" : "answers" : results?.total === 1 ? "vote" : "votes"}</span>
        {quiz && answered ? <span className={styles.feedback} role="status">{results.choice === content.correctOption ? "Correct!" : "Thanks for answering."}</span> : <button type="button" onClick={vote} disabled={locked || selected === null || selected === results?.choice}>{pending ? "Saving…" : quiz ? "Submit answer" : answered ? "Update vote" : "Vote"}</button>}
      </div>
      {quiz && answered && content.explanation ? <p className={styles.explanation}>{content.explanation}</p> : null}
      {!answered ? <p className={styles.hint}>{quiz ? "One attempt. The correct answer is shown after you submit." : "Results appear after voting. You can change your vote."}</p> : null}
    </> : null}
    {error ? <p className={styles.error} role="alert">{error}</p> : null}
  </div>;
}
