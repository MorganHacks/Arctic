import { Geist } from "next/font/google";
import { FormLinkMedia } from "./form-link-media";
import type { FormLinkCard as Card } from "../../../../libs/ui/form-theme";
import styles from "../../../../libs/ui/form-link-card.module.css";

const geist = Geist({ subsets: ["latin"], display: "swap", variable: "--font-geist-sans", preload: false });

export function FormLinkCard({ card }: { card: Card }) {
  return <aside className={`${styles.dock} ${geist.variable}`} aria-label="Featured link"><div className={`${styles.card} ${styles.formCard}`}>
    <FormLinkMedia image={card.image} url={card.url} />
    <a className={styles.copy} href={card.url} target="_blank" rel="noopener noreferrer">
      {card.label ? <span className={styles.label}>{card.label}</span> : null}
      <span className={styles.titleRow}>
        <strong className={styles.title}>{card.title}</strong>
        <span className={styles.arrow} aria-hidden="true">
          <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" focusable="false">
            <path d="M9 6.65032C9 6.65032 15.9383 6.10759 16.9154 7.08463C17.8924 8.06167 17.3496 15 17.3496 15M16.5 7.5L6.5 17.5" />
          </svg>
        </span>
      </span>
    </a>
  </div></aside>;
}
