"use client";

import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { FieldType, FormField, FormProblem, VersionRow } from "@/lib/api";
import { publishForm, saveDraft } from "../actions";
import styles from "./builder.module.css";
import { TYPES, blankField, blankSection, copyOf } from "./fields";
import { PageBreakIcon, Publish, Save, TypeIcon, Warning } from "./icons";
import { Preview } from "./preview";
import { Question } from "./question";

/** How long to wait after the last keystroke before writing. */
const DEBOUNCE_MS = 700;

/** How long a card that has just moved is marked as having moved. */
const SETTLE_MS = 260;

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
  initialFields,
  lockedKeys,
  versions,
  canManage,
}: {
  formName: string;
  formId: string;
  initialFields: FormField[];
  lockedKeys: string[];
  versions: VersionRow[];
  canManage: boolean;
}) {
  const router = useRouter();

  const [fields, setFields] = useState<FormField[]>(initialFields);
  const [status, setStatus] = useState<SaveStatus>("clean");
  const [problems, setProblems] = useState<FormProblem[]>([]);
  const [notice, setNotice] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);

  /**
   * The question that has just been moved, if the movement was recent.
   *
   * Reordering by button gives no sense of travel — the list simply differs
   * from the one that was there a frame ago — and on a form of twenty
   * questions it is genuinely hard to see which card went where. Held by key
   * rather than by index, because the index is the thing that just changed.
   */
  const [settled, setSettled] = useState<string | null>(null);

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

  const locked = useMemo(() => new Set(lockedKeys), [lockedKeys]);

  /**
   * The questions as the screen should draw them.
   *
   * The lock flag comes from the server's list rather than from the flag on
   * the field. They agree in every ordinary case; when they do not, the
   * server's answer is the one that decides what a save is allowed to do, and
   * a screen showing the other would offer a delete button that cannot work.
   */
  const shown = useMemo(
    () =>
      fields.map((field) =>
        field.locked === locked.has(field.key)
          ? field
          : { ...field, locked: locked.has(field.key) },
      ),
    [fields, locked],
  );

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
   * The settle mark is taken off again.
   *
   * Left on, a second press on the same button would not re-run the animation
   * — the class never changed — so the second move of a card would be the one
   * with no motion on it at all.
   */
  useEffect(() => {
    if (settled === null) {
      return;
    }

    const timer = setTimeout(() => setSettled(null), SETTLE_MS);
    return () => clearTimeout(timer);
  }, [settled]);

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

    setSettled(fields[index].key);
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
  for (const field of shown) {
    if (field.type !== "section") {
      asked += 1;
    }

    ordinals.push(asked);
  }

  const anyLocked = shown.some((field) => field.locked);

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

      <div className={styles.pane}>
        <div>
          {/* Said once, here, rather than under each of the ten locked
              questions an application form starts with. Somebody needs to know
              why the controls are missing; they do not need to be told ten
              times on one screen. And a survey carries none, so on one this
              paragraph is a warning about something that is not on the page. */}
          {anyLocked ? (
            <p className={styles.lockNotice}>
              Questions marked <span className="pill sensitive">Locked</span> are
              required by MLH affiliation, in their wording. The API refuses to
              drop or reword one, not just this screen — the alternative is
              finding out at the export, when there is no way to ask several
              hundred people again.
            </p>
          ) : null}

          <ol className={styles.list}>
            {shown.map((field, index) => (
              <Question
                key={field.key}
                field={field}
                index={index}
                ordinal={ordinals[index]}
                count={shown.length}
                problems={byKey.get(field.key) ?? []}
                disabled={!canManage}
                settling={settled === field.key}
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
          <Preview fields={shown} formName={formName} />

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
