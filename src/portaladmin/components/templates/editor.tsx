"use client";

import { EmailPreview } from "./email-preview";
import { Body, Identity, Placeholders } from "./fields";
import styles from "./templates.module.css";
import { useDraft, FROM_DOMAIN, FROM_LOCAL } from "./use-draft";
import { usePreview } from "./use-preview";
import { useSave } from "./use-save";
import type { Placeholder, Template } from "./types";

/**
 * Writing one email template, with the message drawn beside it.
 *
 * This file is the arrangement and nothing else. What is being typed lives in
 * useDraft, what the API makes of it in usePreview, and what happens when
 * somebody presses the button in useSave — three concerns that used to share
 * one four-hundred-line component and, through it, each other's re-renders. A
 * keystroke in the reply-to box scheduled a render of a body that had not
 * moved, because the effect that renders sat next to the state that changed.
 *
 * The three are split along what they depend on rather than by what they are
 * called. useDraft touches every field; usePreview touches three of them;
 * useSave touches all of them once, when asked. That is the seam, and it is
 * why they are separable at all.
 *
 * The preview is the API's. There is no markdown in this file on purpose: two
 * renderers agree until the day somebody types the thing they disagree about,
 * and the one that matters is the one that sends.
 */
export function Editor({
  template,
  canManage,
  available,
}: {
  template: Template | null;
  canManage: boolean;
  /**
   * The placeholders a send can fill in, or null where the API could not say.
   *
   * Read on the server by the page rather than fetched from here, so the menu
   * is available on the first keystroke instead of after a round trip that
   * would land somewhere in the middle of the first sentence.
   *
   * Null is not an empty list. Empty means the API answered and there is
   * nothing to offer; null means nobody knows, and the difference decides
   * whether a name the author typed can be called unknown.
   */
  available: Placeholder[] | null;
}) {
  const handle = useDraft(template, available);
  const { draft } = handle;

  const preview = usePreview(template, draft.subject, draft.body, draft.format);
  const saving = useSave(template, handle.toRequest);

  return (
    <div className={styles.editor}>
      <div>
        <fieldset className={styles.form} disabled={!canManage}>
          <Identity
            handle={handle}
            existingKey={template?.key ?? null}
            available={available}
          />
          <Body handle={handle} available={available} />
          <Placeholders handle={handle} />
        </fieldset>

        {canManage ? <Actions template={template} saving={saving} /> : null}
      </div>

      <div className={styles.sticky}>
        <EmailPreview
          fromName={draft.fromName.trim() === "" ? null : draft.fromName.trim()}
          fromLocal={FROM_LOCAL}
          fromDomain={FROM_DOMAIN}
          replyTo={draft.replyTo.trim() === "" ? null : draft.replyTo}
          rendered={preview.rendered}
          pending={preview.pending}
          error={preview.error}
        />
      </div>
    </div>
  );
}

/**
 * The button, and the question an edit has to answer first.
 *
 * Creating does not ask. There is nothing yet to disagree with, and a
 * confirmation in front of the first save is a step that teaches people to
 * click through confirmations.
 */
function Actions({
  template,
  saving,
}: {
  template: Template | null;
  saving: ReturnType<typeof useSave>;
}) {
  return (
    <>
      {saving.asked ? (
        <div className={styles.confirm}>
          {/* Not a warning about this screen. A campaign renders its messages
              when it is queued, so what has already gone out keeps the wording
              it had — which means an edit here can leave the template
              disagreeing with the email somebody received. */}
          <p>
            Saving writes a new version. A campaign that has already gone out
            sent the wording this template had then, not this.
          </p>
          <div className={styles.actions} style={{ marginTop: 0 }}>
            <button
              type="button"
              className="button primary"
              onClick={saving.save}
              disabled={saving.saving}
            >
              {saving.saving ? "Saving…" : "Confirm save"}
            </button>
            <button type="button" onClick={() => saving.ask(false)}>
              Cancel
            </button>
          </div>
        </div>
      ) : (
        <div className={styles.actions}>
          <button
            type="button"
            className="button primary"
            onClick={template ? () => saving.ask(true) : saving.save}
            disabled={saving.saving}
          >
            {template
              ? "Save changes"
              : saving.saving
                ? "Creating…"
                : "Create template"}
          </button>
          {template ? (
            <span className="meta">Version {template.version}</span>
          ) : null}
        </div>
      )}

      {saving.outcome ? (
        <p
          role="status"
          className={
            saving.outcome.ok
              ? styles.saved
              : `${styles.saved} ${styles.failed}`
          }
        >
          {saving.outcome.text}
        </p>
      ) : null}
    </>
  );
}
