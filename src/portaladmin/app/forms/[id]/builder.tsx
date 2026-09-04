"use client";

import { useRouter } from "next/navigation";
import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import type { FieldType, FormField, FormProblem, VersionRow } from "@/lib/api";
import { publishForm, saveDraft } from "../actions";
import { Audience } from "./audience";
import styles from "./builder.module.css";
import { TYPES, blankField, blankSection, copyOf } from "./fields";
import { PageBreakIcon, Publish, Save, TypeIcon, Warning } from "./icons";
import { Preview } from "./preview";
import { Question } from "./question";
import { Schedule } from "./schedule";
import { Unpublish } from "./unpublish";

/** How long to wait after the last keystroke before writing. */
const DEBOUNCE_MS = 700;


/**
 * What the bar says about the work.
 *
 * `dirty` exists because of the debounce. Without it the bar reads "Saved" for
 * the seven hundred milliseconds between a keystroke and the write, which is
 * exactly the window in which somebody closing the tab would lose what they
 * typed — and the screen would have told them it was safe.
 */
type SaveStatus = "clean" | "dirty" | "saving" | "saved" | "failed";

export function Builder({
  formName,
  formId,
  formKind,
  initialFields,
  statuses,
  requiresSignIn,
  eligibleStatuses,
  closesAt,
  published,
  versions,
  canManage,
}: {
  formName: string;
  formId: string;

  /** Which kind of form this is, which decides whether it can have an audience. */
  formKind: string;
  initialFields: FormField[];

  /** Every application status, for the audience panel to offer. */
  statuses: string[];
  requiresSignIn: boolean;
  eligibleStatuses: string[];

  /** When it stops accepting answers, as an instant, or null for no deadline. */
  closesAt: string | null;

  /** Whether there is a live version at all, which is what unpublishing needs. */
  published: boolean;
  versions: VersionRow[];
  canManage: boolean;
}) {
  const router = useRouter();

  const [fields, setFields] = useState<FormField[]>(initialFields);
  const [status, setStatus] = useState<SaveStatus>("clean");
  const [problems, setProblems] = useState<FormProblem[]>([]);
  const [notice, setNotice] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);

  /** The list of question cards, for measuring one against its next position. */
  const list = useRef<HTMLOListElement>(null);

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

  /**
   * Counts edits rather than tracking a boolean.
   *
   * The effect below has to fire on every change and not on the first render —
   * mounting is not an edit, and saving on mount would write the draft back
   * unchanged every time anybody opened the page.
   */
  const [edits, setEdits] = useState(0);

  /**
   * The save this component is waiting on.
   *
   * Debouncing makes overlapping saves rare rather than impossible: a slow
   * write and a fast one started after it can land out of order, and the older
   * answer would then overwrite the newer one's problems on screen. Only the
   * newest attempt is allowed to speak.
   */
  const attempt = useRef(0);

  /**
   * The edit number already on disk.
   *
   * Saving by hand and publishing both write immediately, which leaves a
   * debounce timer already running with nothing left to say. Without this it
   * fires anyway and writes the same questions a second time.
   */
  const written = useRef(0);

  const write = useCallback(
    async (next: FormField[]) => {
      const mine = (attempt.current += 1);
      written.current = edits;
      setStatus("saving");

      const result = await saveDraft(formId, next);

      // A reply from a save that has already been superseded says nothing
      // useful about what is on screen now.
      if (mine !== attempt.current) {
        return result;
      }

      setStatus(result.ok ? "saved" : "failed");
      setProblems(result.problems);
      setNotice(result.error ?? null);
      return result;
    },
    [formId, edits],
  );

  useEffect(() => {
    if (edits === 0 || !canManage) {
      return;
    }

    const timer = setTimeout(() => {
      // Read at the moment it fires rather than when it was set, because
      // what has been written may have changed in between.
      if (written.current === edits) {
        return;
      }

      void write(fields);
    }, DEBOUNCE_MS);

    return () => clearTimeout(timer);
  }, [edits, fields, canManage, write]);

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
    setFields(next);
    setEdits((n) => n + 1);
    setStatus("dirty");
    setNotice(null);
  }, []);

  const patch = (index: number, changes: Partial<FormField>) =>
    change((current) =>
      current.map((field, i) => (i === index ? { ...field, ...changes } : field)),
    );

  const move = (index: number, delta: number) => {
    const to = index + delta;
    if (to < 0 || to >= fields.length) {
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

    change((current) => {
      const next = [...current];
      [next[index], next[to]] = [next[to], next[index]];
      return next;
    });
  };

  const remove = (index: number) =>
    change((current) => current.filter((_, i) => i !== index));

  // Straight after the one it came from, which is where somebody making a
  // third variant of the same question is already looking.
  const duplicate = (index: number) =>
    change((current) => [
      ...current.slice(0, index + 1),
      copyOf(current[index]),
      ...current.slice(index + 1),
    ]);

  const add = (type: FieldType) =>
    change((current) => [...current, blankField(type)]);

  // Appended like a question, because it is a field in the same array and
  // moves with the same two buttons. Everything after it is the next page, so
  // adding one at the bottom and moving it up is how a form gets split.
  const addSection = () => change((current) => [...current, blankSection()]);

  async function publish() {
    setPublishing(true);
    setNotice(null);

    try {
      // The debounce means what is on screen may not be what is on disk, and
      // publishing what is on disk would silently drop the last few seconds of
      // typing into a version several hundred people then answer. Written
      // first, deliberately, even though it usually changes nothing.
      const saved = await write(fields);
      if (!saved.ok) {
        return;
      }

      const result = await publishForm(formId);
      setProblems(result.problems);

      if (!result.ok) {
        setNotice(result.error ?? "This form could not be published.");
        return;
      }

      setNotice("Published. Applicants following the link see this now.");
      setStatus("clean");

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

  // Problems that name a key no longer on the form, plus the ones that never
  // named one. Without this a complaint about a question somebody has since
  // deleted would simply vanish, and "publish did nothing" is the worst
  // possible answer.
  const loose = problems.filter(
    (problem) =>
      problem.fieldKey === null ||
      !fields.some((field) => field.key === problem.fieldKey),
  );

  // The number shown against each question, counting only the questions. Page
  // breaks live in the same array, so numbering by position would leave gaps
  // that read as a question having gone missing.
  const ordinals: number[] = [];
  let asked = 0;
  for (const field of fields) {
    if (field.type !== "section") {
      asked += 1;
    }

    ordinals.push(asked);
  }

  return (
    <>
      <div className={styles.toolbar}>
        <span className={status === "failed" ? styles.saveFailed : styles.save}>
          <span className={`${styles.dot} ${DOT_CLASS(status)}`} />
          {SAVE_LABELS[status]}
        </span>
        <span className={styles.spacer} />

        {canManage ? (
          <>
            {/* The debounce covers the ordinary case; this covers the one it
                cannot. A save that failed leaves nothing to press, and
                "Not saved" with no way to try again is worse than no bar at
                all. */}
            <button
              type="button"
              className={styles.toolbarButton}
              disabled={status === "saving" || publishing}
              onClick={() => void write(fields)}
            >
              <Save />
              Save now
            </button>
            <button
              type="button"
              className={`button primary ${styles.toolbarButton}`}
              disabled={publishing}
              onClick={publish}
            >
              <Publish />
              {publishing ? "Publishing…" : "Publish"}
            </button>
          </>
        ) : (
          <span className="meta">
            You do not have <code>forms.manage</code>, so this is read-only.
          </span>
        )}
      </div>

      {notice ? <p className="error">{notice}</p> : null}

      {loose.length > 0 ? (
        <div className={`panel ${styles.problemsPanel}`}>
          <h2>Not ready to publish</h2>
          <ul className={styles.problems}>
            {loose.map((problem) => (
              <li key={problem.message}>
                <Warning />
                {problem.message}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {/*
       * Who the form is for, before the questions rather than beside them.
       *
       * This band is the settings a form has that are not questions, and it is
       * above the editor because that is the order the decisions happen in.
       * A grid rather than a row so it collapses to one column on a narrow
       * screen without anything being told how many neighbours it has.
       */}
      <div className={styles.settings}>
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
      </div>

      <div className={styles.pane}>
        <div>
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

          <ol className={styles.list} ref={list}>
            {fields.map((field, index) => (
              <Question
                key={field.key}
                field={field}
                index={index}
                ordinal={ordinals[index]}
                count={fields.length}
                problems={byKey.get(field.key) ?? []}
                disabled={!canManage}
                onChange={(changes) => patch(index, changes)}
                onMove={(delta) => move(index, delta)}
                onDuplicate={() => duplicate(index)}
                onRemove={() => remove(index)}
              />
            ))}
          </ol>

          {canManage ? (
            <div className={styles.picker}>
              {/*
               * Every type on the screen at once rather than behind a menu.
               *
               * The eleven are the vocabulary of this editor and they fit, so a
               * list that has to be opened to be read is a list nobody reads —
               * somebody reaches for Short text forty times and never finds out
               * Date is in there. It is also one press instead of two, which is
               * the smaller of the two wins.
               */}
              <span className={styles.pickerHead} id="add-question">
                Add a question
              </span>

              <div className={styles.pickerGrid} role="group" aria-labelledby="add-question">
                {TYPES.map((type) => (
                  <button
                    key={type.value}
                    type="button"
                    className={styles.pickerBtn}
                    onClick={() => add(type.value)}
                  >
                    <TypeIcon type={type.value} />
                    {type.label}
                  </button>
                ))}
              </div>

              {/* Outside that grid rather than a twelfth button in it. A page
                  break is not a kind of question, and putting it in that list
                  is how somebody turns question nine into a divider by aiming
                  badly — with the answers already given to it still filed
                  under its key. */}
              <button
                type="button"
                className={styles.pickerBreak}
                onClick={addSection}
              >
                <PageBreakIcon size={16} />
                Add page break
              </button>
            </div>
          ) : null}
        </div>

        <aside className={styles.side}>
          <Preview fields={fields} formName={formName} />

          {versions.length > 0 ? (
            <section className={styles.history}>
              <h2>History</h2>
              <ul>
                {versions.map((version) => (
                  <li key={version.version}>
                    <span>
                      v{version.version}{" "}
                      <span className="meta">{version.questions} questions</span>
                    </span>
                    <span className="meta">
                      {version.status}
                      {version.publishedAt
                        ? ` ${version.publishedAt.slice(0, 10)}`
                        : ""}
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          ) : null}
        </aside>
      </div>

      {/*
       * The one control here that cannot be taken back, at the bottom.
       *
       * Deliberately not in the band at the top with the other settings, and
       * not in the toolbar beside Publish. A destructive button placed where
       * the eye lands first is one that gets pressed by a hand aiming for
       * something else, and Publish is pressed dozens of times an afternoon
       * while this is pressed roughly never. The console puts its other
       * irreversible control at the foot of its page for the same reason.
       *
       * Only when there is something to take down. On a form that has never
       * been published it would be a red panel offering to undo nothing.
       */}
      {canManage && published ? (
        <Unpublish
          formId={formId}
          formName={formName}
          onDone={() => router.refresh()}
        />
      ) : null}
    </>
  );
}

const SAVE_LABELS: Record<SaveStatus, string> = {
  clean: "Saved",
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
    clean: styles.dotClean,
    dirty: styles.dotDirty,
    saving: styles.dotSaving,
    saved: styles.dotSaved,
    failed: styles.dotFailed,
  })[status];
