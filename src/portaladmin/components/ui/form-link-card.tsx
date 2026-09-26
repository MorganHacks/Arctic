import { ArrowUpRight01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "./icon";
import { FormLinkMedia } from "./form-link-media";
import type { FormLinkCard as Card } from "../../../../libs/ui/form-theme";
import styles from "../../../../libs/ui/form-link-card.module.css";

export function FormLinkCard({ card, interactive = true, floating = false }: { card: Card; interactive?: boolean; floating?: boolean }) {
  const copy = <>
      {card.label ? <span className={styles.label}>{card.label}</span> : null}
      <span className={styles.titleRow}>
        <strong className={styles.title}>{card.title}</strong>
        <span className={styles.arrow} aria-hidden="true"><Icon icon={ArrowUpRight01Icon} size={11} strokeWidth={2} /></span>
      </span>
  </>;
  const content = <div className={`${styles.card} ${styles.formCard}`}>
    <FormLinkMedia image={card.image} url={card.url} />
    {interactive ? <a className={styles.copy} href={card.url} target="_blank" rel="noopener noreferrer">{copy}</a>
      : <span className={styles.copy}>{copy}</span>}
  </div>;
  return floating ? <aside className={styles.dock} aria-label="Featured link">{content}</aside> : content;
}
