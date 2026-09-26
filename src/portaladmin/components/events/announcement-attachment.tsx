import { announcementLabels, videoEmbed, type AnnouncementContent, type AnnouncementResults } from "../../../../libs/ui/announcements";
import styles from "./announcement-attachment.module.css";

export function AnnouncementAttachment({ content, results }: { content: AnnouncementContent | null; results: AnnouncementResults | null }) {
  if (!content) return null;
  const embed = content.kind === "video" ? videoEmbed(content.media?.[0]?.url ?? "") : null;
  return <div className={styles.attachment}>
    <span className={styles.kind}>{announcementLabels[content.kind]}</span>
    {content.kind === "image" ? <div className={styles.images}>{content.media?.map((item, index) => <a key={index} href={item.url} target="_blank" rel="noreferrer"><img src={item.url} alt={item.alt || "Announcement image"} loading="lazy" referrerPolicy="no-referrer" /></a>)}</div> : null}
    {content.kind === "video" && content.media?.[0] ? <div className={styles.video}>{embed ? <iframe src={embed} title="Announcement video" allowFullScreen loading="lazy" referrerPolicy="strict-origin-when-cross-origin" /> : <video src={content.media[0].url} controls preload="metadata" />}<a href={content.media[0].url} target="_blank" rel="noreferrer">Open video</a></div> : null}
    {content.options ? <div className={styles.options}>{content.options.map((option, index) => {
      const count = results?.counts?.[index] ?? 0;
      const percent = results?.total ? Math.round(count / results.total * 100) : 0;
      return <div key={index} className={styles.option}>
        {option.imageUrl ? <img src={option.imageUrl} alt={option.text} loading="lazy" referrerPolicy="no-referrer" /> : null}
        <div className={styles.answer}><div className={styles.optionLabel}><span>{option.text}{content.correctOption === index ? <span className={styles.correct}>Correct answer</span> : null}</span><span>{count} · {percent}%</span></div><div className={styles.track}><span style={{ width: `${percent}%` }} /></div></div>
      </div>;
    })}<p className={styles.total}>{results?.total ?? 0} {content.kind === "quiz" ? results?.total === 1 ? "answer" : "answers" : results?.total === 1 ? "vote" : "votes"}{content.explanation ? ` · ${content.explanation}` : ""}</p></div> : null}
  </div>;
}
