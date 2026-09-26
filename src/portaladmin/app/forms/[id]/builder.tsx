"use client";

import { summarizeErrors } from "../../../../../libs/ui/error-notifications";
import { ErrorToast } from "@/components/ui/error-toast";
import { useRouter } from "next/navigation";
import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useReducer,
  useRef,
  useState,
} from "react";
import type { DraftView, FieldType, FormField, FormProblem, FormSummary, VersionRow } from "@/lib/api";
import { Cancel01Icon, CheckmarkCircle02Icon, CollapseIcon, DragDropVerticalIcon, FullScreenIcon, Redo03Icon, Undo03Icon, ViewIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { publishForm } from "../actions";
import { Audience } from "./audience";
import styles from "./builder.module.css";
import { blankField, blankSection, copyOf } from "./fields";
import { FormHeader } from "./form-header";
import { Save } from "./icons";
import { Preview, PreviewActions } from "./preview";
import { Question } from "./question";
import { Schedule } from "./schedule";
import { Unpublish } from "./unpublish";
import { ThemePicker } from "./theme-picker";
import { QuestionToolbar } from "./question-toolbar";
import { VersionHistory } from "./version-history";
import { PublishControl } from "./publish-control";
import { MlhSettings } from "./mlh-settings";
import { useQuestionDrag } from "./use-question-drag";
import { reorderFields } from "./reorder-fields";
import { createEditorHistory, editorHistory, type EditorDraft } from "./editor-history";
import { useFormAutosave } from "./use-form-autosave";
import type { SaveStatus } from "./draft-autosave";
import { formThemeStyle, resolveFormTheme, type FormTheme } from "../../../../../libs/ui/form-theme";

export function Builder({
  ownerId,
  form,
  draftVersion,
  responseCount,
  mlhSeason,
  initialTheme,
  initialFields,
  statuses,
  requiresSignIn,
  eligibleStatuses,
  closesAt,
  published,
  versions,
  canManage,
}: {
  ownerId: string;
  form: FormSummary;
  draftVersion: number;
  responseCount: number;
  mlhSeason: number | null;
  initialTheme?: FormTheme;
  initialFields: FormField[];

  /** Every application status, for the audience panel to offer. */
  statuses: string[];
  requiresSignIn: boolean;
  eligibleStatuses: string[];

  /** When it stops accepting answers, as an instant, or null for no deadline. */
  closesAt: string | null;

  published: DraftView["published"];
  versions: VersionRow[];
  canManage: boolean;
}) {
  const router = useRouter();
  const { id: formId, name: formName, kind: formKind } = form;

  const [history, dispatch] = useReducer(editorHistory, { fields: initialFields, theme: resolveFormTheme(initialTheme) }, createEditorHistory);
  const { fields, theme } = history.present;
  const [problems, setProblems] = useState<FormProblem[]>([]);
  const [notice, setNotice] = useState<string | null>(null);
  const [toast, setToast] = useState<{ title: string; detail: string } | null>(null);
  const [publishing, setPublishing] = useState(false);
  const [confirmUnpublish, setConfirmUnpublish] = useState(false);
  const [previewOnly, setPreviewOnly] = useState(false);
  const [editorExpanded, setEditorExpanded] = useState(false);
  const [activeKey, setActiveKey] = useState<string | null>(null);
  const canvas = useRef<HTMLDivElement>(null);
  const editorScroll = useRef(0);
  const addedField = useRef<string | null>(null);

  useLayoutEffect(() => {
    if (canvas.current) canvas.current.scrollTop = previewOnly ? 0 : editorScroll.current;
  }, [previewOnly]);

  /** The list of question cards, for measuring one against its next position. */
  const list = useRef<HTMLOListElement>(null);

  useLayoutEffect(() => {
    const key = addedField.current;
    if (!key) return;
    const card = Array.from(list.current?.children ?? []).find(element => element instanceof HTMLElement && element.dataset.key === key);
    if (!(card instanceof HTMLElement)) return;
    addedField.current = null;
    card.scrollIntoView({ block: "start", behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth" });
    card.querySelector<HTMLElement>('textarea, input:not([type="checkbox"])')?.focus({ preventScroll: true });
  }, [fields]);

  /**
   * Where each card was before the reorder that is about to be rendered.
   *
   * Filled in by `move` and emptied by the layout effect that reads it, so it
   * is non-empty for exactly the one render that follows a reorder. An edit
   * that is not a reorder never fills it and therefore never animates: a card
   * that grows because somebody added an option pushes its neighbours down,
   * and sliding them for that would be motion attached to typing.
   */
  const before = useRef(new Map<string, number>());

  /**
   * The button that caused the reorder.
   *
   * Reordering has to be repeatable from a keyboard without hunting for the
   * button again, and the card being moved through the list is the card the
   * focus is sitting in. React moves the existing row rather than rebuilding
   * it, which keeps the focus by itself in every browser we have tried; this
   * puts it back if one ever does not.
   */
  const pressed = useRef<HTMLElement | null>(null);

  const restoreDraft = useCallback((draft: EditorDraft) => dispatch({ type: "restore", draft }), []);
  const autosave = useFormAutosave({
    ownerId, formId, version: draftVersion, draft: history.present, revision: history.revision,
    enabled: canManage, onRestore: restoreDraft,
  });
  const { status } = autosave;
  const canEdit = canManage && !autosave.conflict;

  useEffect(() => { setNotice(null); }, [history.revision]);

  useEffect(() => {
    if (!toast) return;
    const timer = window.setTimeout(() => setToast(null), 5000);
    return () => window.clearTimeout(timer);
  }, [toast]);
  useEffect(() => { setProblems(autosave.result.problems); }, [autosave.result]);

  /*
   * The card travels to its new place instead of appearing in it.
   *
   * Reordering by button gives no sense of travel: the list simply differs
   * from the one that was there a frame ago, and on a form of twenty questions
   * it is genuinely hard to see which card went where and which one it swapped
   * with. Both cards move, because both of them did.
   *
   * The standard four steps. `move` measured every card before changing the
   * order; by the time this runs the browser has laid the new order out but
   * has not painted it, so each card is put back at the offset it came from
   * with the transition suppressed, the offsets are flushed in one read, and
   * then they are dropped and the stylesheet's transition carries the card
   * home. A layout effect rather than an ordinary one for exactly that reason
   * — after paint, the jump has already been seen.
   *
   * Nothing here consults prefers-reduced-motion. The travel is a CSS
   * transition on `.card`, so the blanket rule in libs/ui/tokens.css collapses
   * its duration and the card arrives in place, which is the same thing this
   * did before the animation existed.
   */
  useLayoutEffect(() => {
    const was = before.current;
    if (was.size === 0) {
      return;
    }

    before.current = new Map();

    const moved: HTMLElement[] = [];
    for (const card of list.current?.children ?? []) {
      if (!(card instanceof HTMLElement)) {
        continue;
      }

      const from = was.get(card.dataset.key ?? "");
      const shift = from === undefined ? 0 : from - card.getBoundingClientRect().top;
      if (shift === 0) {
        continue;
      }

      card.style.transition = "none";
      card.style.transform = `translateY(${shift}px)`;
      moved.push(card);
    }

    if (moved.length > 0) {
      // One forced reflow for the whole list rather than one per card. This
      // read is what makes the offsets above the transition's starting point
      // instead of a style change the browser coalesces away.
      void list.current?.offsetHeight;

      for (const card of moved) {
        card.style.transition = "";
        card.style.transform = "";
      }
    }

    // Only ever a no-op in the browsers we have: React moves the row rather
    // than rebuilding it, so the focus went with it.
    const button = pressed.current;
    pressed.current = null;
    if (button?.isConnected && document.activeElement !== button) {
      button.focus();
    }
  }, [fields]);

  /** Every mutation goes through here, so nothing can change without saving. */
  const change = useCallback((next: (current: FormField[]) => FormField[]) => {
    dispatch({ type: "fields", update: next });
  }, []);

  const changeTheme = (next: FormTheme) => {
    if (JSON.stringify(next) === JSON.stringify(theme)) return;
    dispatch({ type: "theme", theme: next });
  };

  const restore = useCallback((type: "undo" | "redo") => {
    dispatch({ type });
    setProblems([]);
  }, []);

  useEffect(() => {
    if (!canEdit || publishing || previewOnly) return;
    function keyboard(event: KeyboardEvent) {
      if (!(event.metaKey || event.ctrlKey) || event.altKey) return;
      const target = event.target;
      if (target instanceof Element && target.closest('input, textarea, select, [contenteditable="true"]')) return;
      const key = event.key.toLowerCase();
      const redo = key === "y" || key === "z" && event.shiftKey;
      if (key !== "z" && key !== "y") return;
      if (redo ? !history.future.length : !history.past.length) return;
      event.preventDefault();
      restore(redo ? "redo" : "undo");
    }
    window.addEventListener("keydown", keyboard);
    return () => window.removeEventListener("keydown", keyboard);
  }, [canEdit, publishing, previewOnly, history.future.length, history.past.length, restore]);

  const patch = (index: number, changes: Partial<FormField>) =>
    change((current) =>
      current.map((field, i) => (i === index ? { ...field, ...changes } : field)),
    );

  const reorder = (key: string, to: number) => {
    const from = fields.findIndex(field => field.key === key);
    if (from < 0 || from === to || to < 0 || to >= fields.length) {
      return;
    }

    // Measured before the order changes, which is the whole trick: after this
    // returns, React writes the new order and the layout effect above has the
    // two numbers it needs to put each card back where it started.
    const was = new Map<string, number>();
    for (const card of list.current?.children ?? []) {
      if (card instanceof HTMLElement && card.dataset.key) {
        was.set(card.dataset.key, card.getBoundingClientRect().top);
      }
    }

    before.current = was;
    pressed.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;

    change(current => reorderFields(current, key, to));
  };

  const move = (index: number, delta: number) => reorder(fields[index].key, index + delta);
  const { drag, announcement, instructionId, handleProps } = useQuestionDrag({
    fields, list, canvas, disabled: !canEdit || publishing || previewOnly, onReorder: reorder,
  });

  const remove = (index: number) =>
    change((current) => current.filter((_, i) => i !== index));

  // Straight after the one it came from, which is where somebody making a
  // third variant of the same question is already looking.
  const duplicate = (index: number) => {
    const copy = copyOf(fields[index]);
    setActiveKey(copy.key);
    change((current) => [
      ...current.slice(0, index + 1),
      copy,
      ...current.slice(index + 1),
    ]);
  };

  const add = (type: FieldType) => {
    const field = blankField(type);
    addedField.current = field.key;
    setActiveKey(field.key);
    change((current) => [...current, field]);
  };

  // Appended like a question, because it is a field in the same array and
  // moves with the same two buttons. Everything after it is the next page, so
  // adding one at the bottom and moving it up is how a form gets split.
  const addSection = () => {
    const field = blankSection();
    addedField.current = field.key;
    change(current => [...current, field]);
  };

  async function publish() {
    setPublishing(true);
    setNotice(null);
    setToast(null);

    try {
      // The debounce means what is on screen may not be what is on disk, and
      // publishing what is on disk would silently drop the last few seconds of
      // typing into a version several hundred people then answer. Written
      // first, deliberately, even though it usually changes nothing.
      const saved = await autosave.save();
      if (!saved.ok) {
        return;
      }

      const result = await publishForm(formId);
      setProblems(result.problems);

      if (!result.ok) {
        setNotice(result.error ?? "This form could not be published.");
        return;
      }

      setToast({ title: "Form published", detail: "Your latest changes are live." });

      // Pulls the new version numbers and the new history down. The questions
      // do not change — the next draft is seeded from what was just published
      // — so the editor keeps its state and nothing moves under the cursor.
      router.refresh();
    } finally {
      setPublishing(false);
    }
  }

  const byKey = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const problem of problems) {
      if (problem.fieldKey === null) {
        continue;
      }

      map.set(problem.fieldKey, [...(map.get(problem.fieldKey) ?? []), problem.message]);
    }

    return map;
  }, [problems]);

  const errorMessage = summarizeErrors([notice ?? autosave.result.error ?? "", ...problems.map(problem => {
    const field = fields.find(item => item.key === problem.fieldKey);
    return field ? `${field.label || "Untitled question"}: ${problem.message}` : problem.message;
  })], "questions");

  // The number shown against each question, counting only the questions. Page
  // breaks live in the same array, so numbering by position would leave gaps
  // that read as a question having gone missing.
  const ordinals: number[] = [];
  const pageNumbers: number[] = [];
  let asked = 0;
  let pageCount = 1;
  for (const [index, field] of fields.entries()) {
    if (field.type !== "section") {
      asked += 1;
    } else if (index > 0) {
      pageCount += 1;
    }

    ordinals.push(asked);
    pageNumbers.push(pageCount);
  }

  const actions = (
    <div className={styles.headerActions}>
      <span className={status === "failed" ? styles.saveFailed : styles.save} role="status" title="Your draft saves automatically as you edit.">
        <span className={`${styles.dot} ${DOT_CLASS(status)}`} />
        {SAVE_LABELS[status]}
      </span>
      <div className={styles.quickActions}>
        <ThemePicker theme={theme} onChange={changeTheme} disabled={!canEdit || publishing} />
        <button type="button" className={styles.headerIcon} aria-label="Undo" title="Undo (⌘Z / Ctrl+Z)"
          disabled={!canEdit || !history.past.length || publishing} onClick={() => restore("undo")}><Icon icon={Undo03Icon} size={20} /></button>
        <button type="button" className={styles.headerIcon} aria-label="Redo" title="Redo (⌘⇧Z / Ctrl+Shift+Z)"
          disabled={!canEdit || !history.future.length || publishing} onClick={() => restore("redo")}><Icon icon={Redo03Icon} size={20} /></button>
      </div>
      <button type="button" className={styles.toolbarButton} aria-pressed={previewOnly}
        onClick={() => {
          if (!previewOnly) editorScroll.current = canvas.current?.scrollTop ?? 0;
          setPreviewOnly(current => !current);
        }}>
        {previewOnly ? <svg width="16" height="16" viewBox="0 0 12 12" fill="none" aria-hidden="true" focusable="false">
          <g transform="translate(12 0) scale(-1 1)" stroke="currentColor" strokeWidth="1.2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M9 4.50098H5.5C3.61438 4.50098 2.67157 4.50098 2.08578 5.08675C1.5 5.67255 1.5 6.61535 1.5 8.501V10.001" />
            <path d="M6.5 2.00098L9 4.50098L6.5 7.00098" />
          </g>
        </svg> : <Icon icon={ViewIcon} size={16} />}
        {previewOnly ? "Back to editor" : "Preview"}
      </button>

      {canManage ? (
        <>
          <button
            type="button"
            className={styles.toolbarButton}
            disabled={!canEdit || status === "saving" || publishing}
            onClick={() => void autosave.save()}
          >
            <Save />
            Save now
          </button>

        </>
      ) : (
        <span className="meta">
          You do not have <code>forms.manage</code>, so this is read-only.
        </span>
      )}
      <PublishControl published={!!published} publishing={publishing} canManage={canEdit} onPublish={publish}
        onUnpublish={() => setConfirmUnpublish(true)}>
        <Audience
          formId={formId}
          kind={formKind}
          statuses={statuses}
          initialRequiresSignIn={requiresSignIn}
          initialStatuses={eligibleStatuses}
          canManage={canManage}
        />
        <Schedule
          formId={formId}
          closesAt={closesAt}
          canManage={canManage}
          onSaved={() => router.refresh()}
        />
        <MlhSettings enabled={theme.showMlhBadge} season={mlhSeason} eventId={form.eventId}
          color={theme.mlhBadgeColor} onColorChange={mlhBadgeColor => changeTheme({ ...theme, mlhBadgeColor })}
          disabled={!canEdit || publishing} onChange={showMlhBadge => changeTheme({ ...theme, showMlhBadge })} />
      </PublishControl>
    </div>
  );

  return (
    <div className={styles.builder} data-preview={previewOnly} data-expanded={previewOnly || editorExpanded} data-editable={canManage}>
      <FormHeader form={form} published={published} draftVersion={draftVersion} responseCount={responseCount} tab="questions"
        actions={previewOnly ? <PreviewActions code={form.code} published={!!published} /> : actions}
        collapsed={previewOnly || editorExpanded}
        backAction={previewOnly ? <div className={styles.previewNavigation}>
          <button type="button" className={styles.previewBack} aria-label="Back to editor" title="Back to editor" onClick={() => setPreviewOnly(false)}>
            <Icon icon={Undo03Icon} size={19} strokeWidth={2} />
          </button>
          <span>Preview mode</span>
        </div> : undefined} />
      {drag ? <div className={styles.dragPreview} aria-hidden="true" style={{ left: drag.x, top: drag.y }}><Icon icon={DragDropVerticalIcon} size={18} /><span>{drag.label}</span></div> : null}
      <div className={styles.canvas} ref={canvas} style={formThemeStyle({ ...theme, background: "neutral" })}>
        {!previewOnly ? <div className={styles.canvasHistory}>
          <VersionHistory formId={formId} versions={versions} saveStatus={status} />
          <button type="button" className={styles.headerIcon} aria-pressed={editorExpanded}
            aria-label={editorExpanded ? "Collapse editor" : "Expand editor"} title={editorExpanded ? "Collapse editor" : "Expand editor"}
            onClick={() => setEditorExpanded(current => !current)}>
            <Icon icon={editorExpanded ? CollapseIcon : FullScreenIcon} size={18} />
          </button>
        </div> : null}
        <div className={styles.canvasInner}>
          <ErrorToast title="Changes need attention" message={errorMessage} revision={autosave.result} />
          <ErrorToast message={autosave.storageFailed && status !== "saved" ? "Keep this page open until your changes are saved. Browser recovery is unavailable." : null} />
          {autosave.conflict ? <div className={styles.recovery} role="status">
            <p>A draft was recovered, but the saved form has changed. Choose which version to continue editing.</p>
            <div>
              <button type="button" className={styles.toolbarButton} onClick={() => autosave.resolveRecovery(true)}>Restore my changes</button>
              <button type="button" className={styles.toolbarButton} onClick={() => autosave.resolveRecovery(false)}>Keep saved version</button>
            </div>
          </div> : null}

          <div className={styles.editorColumn} hidden={previewOnly}>
            <div className={styles.editorHeading}>
              <h2>Questions <span>{asked}</span></h2>
            </div>
            {/* On an application form only, because a survey starts empty and
                there is nothing on it this describes. The questions on a new one
                look official enough that somebody would otherwise leave a
                question they do not want, so the point of the line is that they
                do not have to. */}
            {formKind === "application" ? (
              <p className={styles.startingNote}>
                An application form starts with a standard set of questions. Edit
                or remove any of them.
              </p>
            ) : null}

            <p id={instructionId} className={styles.dragInstructions}>Drag the handle to move a card. Use the up and down arrow keys to move it with the keyboard, or Home and End to move it to the start or end.</p>
            <div className={styles.dragInstructions} role="status" aria-live="polite">{announcement}</div>
            {theme.headerImage ? <img className={styles.editorHeaderImage} src={theme.headerImage} alt="Form header" /> : null}
            <ol className={styles.list} ref={list} data-dragging={drag ? "true" : undefined}>
              {fields.map((field, index) => (
                <Question
                  key={field.key}
                  field={field}
                  index={index}
                  ordinal={ordinals[index]}
                  pageNumber={pageNumbers[index]}
                  pageCount={pageCount}
                  count={fields.length}
                  problems={byKey.get(field.key) ?? []}
                  disabled={!canEdit || publishing}
                  active={activeKey === field.key}
                  dragging={drag?.key === field.key}
                  dropEdge={drag?.overKey === field.key ? drag.edge : undefined}
                  dragHandle={<button type="button" className={styles.dragHandle} {...handleProps(field, index)}><Icon icon={DragDropVerticalIcon} size={18} /></button>}
                  onActivate={() => setActiveKey(field.key)}
                  onChange={(changes) => patch(index, changes)}
                  onMove={(delta) => move(index, delta)}
                  onDuplicate={() => duplicate(index)}
                  onRemove={() => remove(index)}
                />
              ))}
            </ol>


          </div>

          {previewOnly ? <Preview fields={fields} formName={formName} headerImage={theme.headerImage} linkCard={theme.linkCard} /> : null}


          {!previewOnly && canManage && published ? (
            <p className={styles.publishNote}>
              Your form is live. Edits save to your draft until you publish changes from the menu above.
              Unpublishing stops new responses and keeps existing answers.
            </p>
          ) : null}
        </div>
      </div>
      {!previewOnly && canManage ? <QuestionToolbar onAdd={add} onAddSection={addSection} disabled={!canEdit || publishing} /> : null}
      {confirmUnpublish && canManage && published ? <Unpublish formId={formId} formName={formName}
        onClose={() => setConfirmUnpublish(false)} onDone={() => {
          setConfirmUnpublish(false);
          setToast({ title: "Form unpublished", detail: "Your existing responses are kept." });
          router.refresh();
        }} /> : null}
      <div className={styles.toastRegion} role="status" aria-live="polite" aria-atomic="true">
        {toast ? <div className={styles.publishToast}>
          <Icon icon={CheckmarkCircle02Icon} size={21} className={styles.toastIcon} />
          <div><strong>{toast.title}</strong><p>{toast.detail}</p></div>
          <button type="button" aria-label="Dismiss notification" onClick={() => setToast(null)}>
            <Icon icon={Cancel01Icon} size={15} />
          </button>
        </div> : null}
      </div>
    </div>
  );
}

const SAVE_LABELS: Record<SaveStatus, string> = {
  dirty: "Unsaved changes",
  saving: "Saving…",
  saved: "Saved",
  failed: "Not saved",
};

/**
 * The dot beside the words.
 *
 * The shape stays put while the label changes, so somebody who has been here
 * for an hour has one constant thing to glance at instead of a sentence to
 * re-read. Only the write in flight moves, and it breathes rather than blinks.
 */
const DOT_CLASS = (status: SaveStatus): string =>
  ({
    dirty: styles.dotDirty,
    saving: styles.dotSaving,
    saved: styles.dotSaved,
    failed: styles.dotFailed,
  })[status];
