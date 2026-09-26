import type { ReactNode } from "react";
import styles from "./empty-state.module.css";

export type EmptyStateVariant = "document" | "data" | "media" | "canvas" | "files";

export function EmptyState({ title, description, variant = "document", size = "panel", action, className = "" }: {
  title: string;
  description: ReactNode;
  variant?: EmptyStateVariant;
  size?: "panel" | "page" | "compact";
  action?: ReactNode;
  className?: string;
}) {
  const Heading = size === "page" ? "h2" : "h3";

  return <div className={`${styles.empty} ${styles[size]} ${className}`}>
    <EmptyStateIllustration variant={variant} />
    <Heading className={styles.title}>{title}</Heading>
    <p className={styles.description}>{description}</p>
    {action ? <div className={styles.action}>{action}</div> : null}
  </div>;
}

function EmptyStateIllustration({ variant }: { variant: EmptyStateVariant }) {
  return <div className={styles.illustration} aria-hidden="true">
    {variant === "document" ? <div className={styles.document}>
      <div className={`${styles.bar} ${styles.heading}`} style={{ width: "55%" }} />
      <div className={styles.spacer} />
      {["90%", "78%", "85%"].map(width => <div key={width} className={styles.bar} style={{ width }} />)}
      <div className={styles.spacer} />
      <div className={`${styles.bar} ${styles.subheading}`} style={{ width: "35%" }} />
      {["65%", "52%", "70%"].map(width => <div key={width} className={styles.row}>
        <div className={styles.bullet} /><div className={styles.bar} style={{ width }} />
      </div>)}
    </div> : variant === "data" ? <div className={styles.data}>
      {Array.from({ length: 6 }, (_, row) => <div className={styles.dataRow} key={row}>
        {[62, 72, 80, 90, 100].map((width, column) => <div key={column}
          className={styles.bar} style={{ width: `${width - (row * 7 + column * 3) % 20}%` }} />)}
      </div>)}
    </div> : variant === "media" ? <div className={styles.media}>
      <div /><div /><div />
    </div> : variant === "canvas" ? <div className={styles.canvas}>
      <div className={styles.browserBar}>
        <span /><span /><span /><div className={styles.bar} />
      </div>
      <div className={styles.canvasBody}>
        <div className={`${styles.bar} ${styles.heading}`} />
        <div className={styles.bar} />
        <div className={styles.canvasButton} />
        <div className={styles.canvasCards}>
          {[0, 1].map(card => <div key={card}>
            {[58, 88, 72].map(width => <div key={width} className={styles.bar} style={{ width: `${width}%` }} />)}
          </div>)}
        </div>
      </div>
    </div> : <div className={styles.files}>
      {[68, 52, 62, 44, 72, 54].map((width, index) => <div className={styles.fileRow} key={index} data-nested={index % 3 !== 0}>
        <span className={index % 3 === 0 ? styles.folder : styles.file} />
        <div className={styles.bar} style={{ width: `${width}%` }} />
      </div>)}
    </div>}
  </div>;
}
