"use client";

import { useEffect, useLayoutEffect, useRef, useState } from "react";
import dynamic from "next/dynamic";
import { useSearchParams } from "next/navigation";
import { useSidebarState } from "@/app/sidebar-state";
import { EditorHeader } from "./editor-header";
import { ApiWorkspace } from "./api-workspace";
import { DesignToolbar, type PreviewDevice } from "./design-toolbar";
import { TestEmailDialog } from "./test-email-dialog";
import { ImportEmailDialog } from "./import-email-dialog";
import { FormatPicker, InboxPreview, TemplateSettings } from "./settings";
import settings from "./settings.module.css";
import styles from "./templates.module.css";
import { useDraft, type DraftHandle } from "./use-draft";
import { usePreview } from "./use-preview";
import { useSave } from "./use-save";
import type { EditorStep, Placeholder, Template } from "./types";

const loadDesignWorkspace = () => import("./design-workspace");
const DesignWorkspace = dynamic(() => loadDesignWorkspace().then((module) => module.DesignWorkspace), {
  loading: () => <p className={styles.editorLoading} role="status">Opening editor…</p>,
});

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
  defaultRecipient = "",
  personId,
}: {
  template: Template | null;
  canManage: boolean;
  defaultRecipient?: string;
  personId: string;
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
  const handle = useDraft(template, available, { id: personId, email: defaultRecipient, canManage });
  const { draft } = handle;
  const query = useSearchParams();
  const { collapseSidebar } = useSidebarState();
  const [device, setDevice] = useState<PreviewDevice>("desktop");
  const [testOpen, setTestOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [toolNotice, setToolNotice] = useState("");

  useEffect(() => {
    if (template === null) collapseSidebar();
  }, [template, collapseSidebar]);

  function changeStep(next: EditorStep) {
    const params = new URLSearchParams(window.location.search);
    params.set("step", next);
    window.history.pushState(null, "", `${window.location.pathname}?${params.toString()}`);
  }

  const preview = usePreview(template, draft.subject, draft.body, draft.format, draft.previewText);
  const saving = useSave(template, handle, canManage, changeStep);

  const requestedStep = query.get("step");
  const step: EditorStep = requestedStep === "api" && saving.designComplete ? "api"
    : (requestedStep === "design" || requestedStep === "api") && saving.settingsComplete ? "design" : "settings";
  const editable: DraftHandle = { ...handle, set: (field, value) => {
    handle.set(field, value);
    saving.clearFieldError(field);
  } };
  const form = useRef<HTMLFieldSetElement>(null);
  const workflow = useRef<HTMLDivElement>(null);
  const positions = useRef<Map<string, { x: number; y: number }>>(new Map());

  useLayoutEffect(() => {
    window.scrollTo({ top: 0, behavior: "auto" });
    const next = new Map<string, { x: number; y: number }>();
    const animations: Animation[] = [];
    workflow.current?.querySelectorAll<HTMLElement>("[data-template-motion]").forEach((element) => {
      const name = element.dataset.templateMotion!;
      const rect = element.getBoundingClientRect();
      const position = { x: rect.left + window.scrollX, y: rect.top + window.scrollY };
      const previous = positions.current.get(name);
      next.set(name, position);
      if (previous && !window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
        const x = previous.x - position.x;
        const y = previous.y - position.y;
        if (x || y) animations.push(element.animate([
          { transform: `translate(${x}px, ${y}px)` },
          { transform: "translate(0, 0)" },
        ], { duration: 240, easing: "cubic-bezier(0.22, 1, 0.36, 1)" }));
      }
    });
    positions.current = next;
    return () => animations.forEach((animation) => animation.cancel());
  }, [step]);

  useEffect(() => {
    if (saving.validationAttempt > 0) {
      form.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
    }
  }, [saving.validationAttempt, step]);

  return (
    <div ref={workflow} className={`${styles.workflow} ${step === "design" ? styles.designWorkflow : ""}`}>
      <EditorHeader
        template={template}
        name={draft.name}
        completedSteps={[...(saving.settingsComplete ? [0] : []), ...(saving.designComplete ? [1] : [])]}
        step={step}
        onStep={saving.navigate}
        onPrepareDesign={() => { void loadDesignWorkspace(); }}
        canManage={canManage}
        saving={saving.saving || !handle.ready}
        confirming={saving.asked}
        onSave={() => saving.requestSave(step)}
        designTools={<DesignToolbar device={device} onDevice={setDevice} onImport={() => setImportOpen(true)}
          onSave={saving.saveDraft}
          onTest={() => setTestOpen(true)} busy={saving.saving || saving.asked}
          hasContent={Boolean(draft.body.trim())} />}
      />
      {testOpen ? <TestEmailDialog onClose={() => setTestOpen(false)} toRequest={handle.toRequest} defaultRecipient={defaultRecipient} /> : null}
      {importOpen ? <ImportEmailDialog hasContent={Boolean(draft.body.trim())} onClose={() => setImportOpen(false)}
        onImport={(body) => { editable.set("format", "html"); editable.set("body", body); setToolNotice("HTML imported. Your design is ready to edit."); }} /> : null}
      <span role="status" className={styles.toolNotice}>{toolNotice}</span>
      {canManage && (saving.asked || saving.outcome?.ok === false) ? (
        <div className={styles.headerFeedback}>
          <SaveFeedback saving={saving} />
        </div>
      ) : null}
      <div className={step === "design" ? styles.designContent : undefined}>
        {step === "settings" ? <div className={settings.grid}>
          <fieldset ref={form} className={settings.form} disabled={!canManage || !handle.ready || saving.saving}>
            <TemplateSettings
              handle={editable}
              available={available}
              errors={saving.fieldErrors}
            />
          </fieldset>
          <aside className={settings.aside}>
            <InboxPreview sender={draft.fromName} subject={draft.subject} snippet={draft.previewText.trim() || preview.rendered?.text || ""} />
            <fieldset className={settings.form} disabled={!canManage || !handle.ready || saving.saving}>
              <FormatPicker handle={editable} />
            </fieldset>
          </aside>
        </div> : step === "design" ? <DesignWorkspace handle={editable} available={available} preview={preview} device={device}
          disabled={!canManage || !handle.ready || saving.saving} error={saving.fieldErrors.body} validationAttempt={saving.validationAttempt}
          saveNotice={saving.outcome?.ok ? saving.outcome.text : null} />
          : <ApiWorkspace
            templateKey={saving.key ?? template?.key ?? handle.toRequest().key ?? ""}
            version={saving.version || template?.version || 1}
            kind={draft.kind}
            placeholders={template?.placeholders ?? []}
            defaultRecipient={defaultRecipient}
            draft={handle.toRequest()}
            canManage={canManage}
          />}
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
function SaveFeedback({
  saving,
}: {
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
      ) : null}

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
      {saving.outcome?.conflict && saving.key ? (
        <button type="button" onClick={saving.reloadLatest} disabled={saving.saving}>
          Discard draft and reload latest version
        </button>
      ) : null}
    </>
  );
}
