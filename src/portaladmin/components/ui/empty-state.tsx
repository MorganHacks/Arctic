import styles from "./empty-state.module.css";

export function EmptyState({ title, description, className = "" }: {
  title: string;
  description: string;
  className?: string;
}) {
  return <div className={`${styles.empty} ${className}`}>
    <div className={styles.document} aria-hidden="true">
      <div className={`${styles.bar} ${styles.heading}`} style={{ width: "55%" }} />
      <div className={styles.spacer} />
      {["90%", "78%", "85%"].map((width) => <div key={width} className={styles.bar} style={{ width }} />)}
      <div className={styles.spacer} />
      <div className={`${styles.bar} ${styles.subheading}`} style={{ width: "35%" }} />
      {["65%", "52%", "70%"].map((width) => <div key={width} className={styles.row}>
        <div className={styles.bullet} /><div className={styles.bar} style={{ width }} />
      </div>)}
    </div>
    <h3 className={styles.title}>{title}</h3>
    <p className={styles.description}>{description}</p>
  </div>;
}
