import styles from "./templates.module.css";

/**
 * There are no templates.
 *
 * The state this system is in today, and the reason no mass email has ever
 * been sent from it. The one thing worth saying is what a template is for,
 * because the button above this is where the first one gets written — and not
 * a word of what one should say, which is not this screen's to suggest.
 */
export function NoTemplates() {
  return (
    <div className={styles.noTemplates}>
      <div className={styles.noTemplatesInner}>
        <div className={styles.emptyTemplatePreview} aria-hidden="true">
          <div className={styles.emptyTemplateMock}>
            <div className={`${styles.emptyTemplateBar} ${styles.emptyTemplateSubject}`} />
            <div className={styles.emptyTemplateRule} />
            <div className={styles.emptyTemplateSender}>
              <div className={styles.emptyTemplateAvatar} />
              <div className={styles.emptyTemplateSenderLines}>
                <div className={styles.emptyTemplateBar} />
                <div className={styles.emptyTemplateBar} />
              </div>
            </div>
            <div className={`${styles.emptyTemplateBar} ${styles.emptyTemplateLineWide}`} />
            <div className={`${styles.emptyTemplateBar} ${styles.emptyTemplateLineMedium}`} />
            <div className={`${styles.emptyTemplateBar} ${styles.emptyTemplateLineShort}`} />
            <div className={styles.emptyTemplateAction} />
          </div>
        </div>
        <p className={styles.noTemplatesTitle}>No templates yet</p>
        <p className={styles.noTemplatesDescription}>
          Create a reusable email for campaigns and automated messages.
        </p>
      </div>
    </div>
  );
}
