import { announcementReactions, emptyReactions, type AnnouncementReactions } from "../../../../libs/ui/announcement-reactions";
import styles from "../../../../libs/ui/announcement-reactions.module.css";

export function AudienceReactions({ reactions }: { reactions: AnnouncementReactions }) {
  const tally = reactions ?? emptyReactions();
  return <div className={styles.row} role="group" aria-label="Audience reactions">
    {announcementReactions.filter(item => tally.counts[item.key] > 0).map(item => <span key={item.key} className={styles.chip}
      aria-label={`${item.label}: ${tally.counts[item.key]} ${tally.counts[item.key] === 1 ? "person" : "people"}`} title={item.label}>
      <span className={styles.emoji} aria-hidden="true">{item.emoji}</span>{tally.counts[item.key]}
    </span>)}
    <span className={styles.caption}>{tally.total ? `${tally.total} audience ${tally.total === 1 ? "reaction" : "reactions"}` : "No reactions yet"}</span>
  </div>;
}
