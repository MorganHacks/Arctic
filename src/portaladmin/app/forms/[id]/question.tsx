"use client";

import type { FieldOption, FormField } from "@/lib/api";
import styles from "./builder.module.css";
import { CHOICE_TYPES, TYPES, TYPE_NAMES, nextOptionValue } from "./fields";
import {
  ArrowDown,
  ArrowUp,
  Cross,
  Duplicate,
  Info,
  Lock,
  PageBreakIcon,
  Plus,
  Trash,
  Warning,
} from "./icons";

/**
 * One question, open for editing.
 *
 * A locked question renders through the same component rather than a separate
 * read-only one. Two components would drift, and the drift would be a locked
 * question that had quietly become editable on a screen nobody was looking at.
 */
export function Question({
  field,
  index,
  ordinal,
  count,
  problems,
  disabled,
  settling,
  onChange,
  onMove,
  onDuplicate,
  onRemove,
}: {
  field: FormField;
  index: number;

  /**
   * Which question this is, counting only the questions.
   *
   * Not `index + 1`. Page breaks sit in the same array, so numbering by
   * position puts gaps in the list — and a form that jumps from 4 to 6 reads
   * as a question having gone missing rather than as a page having started.
   */
  ordinal: number;

  count: number;
  problems: string[];
  disabled: boolean;

  /** True for the moment after this card was moved, and no longer. */
  settling: boolean;

  onChange: (changes: Partial<FormField>) => void;
  onMove: (delta: number) => void;
  onDuplicate: () => void;
  onRemove: () => void;
}) {
  // Locked is what MLH's affiliation requires; disabled is not holding
  // forms.manage. They look the same to a hand on a keyboard and read very
  // differently, so only one of them gets an explanation.
  const frozen = field.locked || disabled;
  const choices = CHOICE_TYPES.has(field.type);

  // A page break is a divider, not something to answer, and the editor has to
  // say so at a glance — an author scanning a form needs to see where the
  // pages fall without reading a type dropdown on every row. Everything below
  // this point is about an answer: a type, a required toggle, options, a
  // storage key. None of it applies, and the API refuses a page break that
  // carries any of it, so it is not offered rather than offered and ignored.
  if (field.type === "section") {
    return (
      <PageBreak
        field={field}
        index={index}
        count={count}
        problems={problems}
        disabled={disabled}
        settling={settling}
        onChange={onChange}
        onMove={onMove}
        onRemove={onRemove}
      />
    );
  }

  const setOption = (at: number, changes: Partial<FieldOption>) =>
    onChange({
      options: field.options.map((option, i) =>
        i === at ? { ...option, ...changes } : option,
      ),
    });

  // Options are ordered on purpose — "Prefer not to say" belongs at the
  // bottom of a list and not wherever it happened to be typed — so the order
  // has to be changeable after the fact, and by the same two buttons the
  // questions use rather than by dragging.
  const moveOption = (at: number, delta: number) => {
    const to = at + delta;
    if (to < 0 || to >= field.options.length) {
      return;
    }

    const next = [...field.options];
    [next[at], next[to]] = [next[to], next[at]];
    onChange({ options: next });
  };

  return (
    <li className={cardClass(styles.card, problems.length > 0, settling)}>
      <div className={styles.cardHead}>
        <span className={styles.ordinal}>{ordinal}</span>

        {/* No echo of the wording here. On an editable question the label
            field is the line directly below this one and on a locked one the
            full text is, so a heading would be the same string printed twice a
            centimetre apart — which reads as a bug rather than as a title, and
            on MLH's sixty-word agreement it would be sixty words truncated to
            four. */}
        {field.required ? (
          <span className={styles.req} aria-hidden="true">
            *
          </span>
        ) : null}

        <span className={styles.spacer} />

        {/* What an answer is filed under. Shown because it is the column
            header in an export and the property name in the stored answer, and
            somebody will eventually have to match one to the other. Never
            editable, and never hidden behind a hover — this is the string
            somebody is squinting at when a spreadsheet does not line up, and a
            keyboard cannot hover. */}
        <code className={styles.key} title="Answers are stored under this key">
          {field.key}
        </code>

        {field.locked ? (
          <span className="pill lapsed">{TYPE_NAMES.get(field.type)}</span>
        ) : (
          <select
            aria-label="Question type"
            className={styles.type}
            value={field.type}
            disabled={disabled}
            onChange={(event) =>
              onChange({
                type: event.target.value as FormField["type"],
                // Options are dropped when a question stops being a choice,
                // because leaving them would mean a paragraph question that
                // still refuses to publish over a duplicate option nobody can
                // see any more.
                options: CHOICE_TYPES.has(event.target.value as FormField["type"])
                  ? field.options.length > 0
                    ? field.options
                    : [{ value: "option_1", label: "Option 1" }]
                  : [],
              })
            }
          >
            {TYPES.map((type) => (
              <option key={type.value} value={type.value}>
                {type.label}
              </option>
            ))}
          </select>
        )}

        <div className={styles.tools}>
          {/* Up and down rather than dragging. Dragging needs a library, and
              these lists are twelve questions long — two buttons are faster to
              aim at than a drop target and, unlike a drop target, they work
              from a keyboard on a screen somebody sits at for hours. */}
          <button
            type="button"
            className={styles.iconBtn}
            aria-label="Move up"
            disabled={disabled || index === 0}
            onClick={() => onMove(-1)}
          >
            <ArrowUp />
          </button>
          <button
            type="button"
            className={styles.iconBtn}
            aria-label="Move down"
            disabled={disabled || index === count - 1}
            onClick={() => onMove(1)}
          >
            <ArrowDown />
          </button>

          {/* Not offered on a locked question. A copy of one is not one — it
              carries MLH's wording without MLH's guarantee that the wording
              stays, and a form with two of the same agreement on it is a form
              somebody answers twice. */}
          {field.locked ? (
            <span
              className={`pill sensitive ${styles.lockPill}`}
              title="Required by MLH affiliation"
            >
              <Lock /> Locked
            </span>
          ) : (
            <>
              <button
                type="button"
                className={styles.iconBtn}
                aria-label="Duplicate"
                disabled={disabled}
                onClick={onDuplicate}
              >
                <Duplicate />
              </button>
              <button
                type="button"
                className={`${styles.iconBtn} ${styles.iconDanger}`}
                aria-label="Delete question"
                disabled={disabled}
                onClick={onRemove}
              >
                <Trash />
              </button>
            </>
          )}
        </div>
      </div>

      {field.locked ? (
        <>
          {/* One short line rather than the whole reason. Ten locked questions
              sit together at the top of every application form, and the full
              explanation repeated ten times is a wall somebody scrolls past.
              It is stated once, above the list. */}
          <p className={styles.lockNote}>
            <Info />
            MLH&rsquo;s wording, and not ours to change.
          </p>
          <p className={styles.lockedLabel}>{field.label}</p>
        </>
      ) : (
        <div className={styles.fields}>
          <div className={styles.field}>
            <label htmlFor={`${field.key}-label`}>Question</label>
            <input
              id={`${field.key}-label`}
              value={field.label}
              disabled={disabled}
              placeholder="What are you asking?"
              onChange={(event) => onChange({ label: event.target.value })}
            />
          </div>

          <div className={styles.field}>
            <label htmlFor={`${field.key}-help`}>Help text</label>
            <input
              id={`${field.key}-help`}
              value={field.help ?? ""}
              disabled={disabled}
              placeholder="Optional. For the thing people always ask."
              onChange={(event) =>
                onChange({ help: event.target.value === "" ? null : event.target.value })
              }
            />
          </div>
        </div>
      )}

      {choices ? (
        <div className={styles.options}>
          <label>Options</label>

          <div className={styles.optionList}>
            {field.options.map((option, at) => (
              /* Keyed by the stored value rather than by position, so
                 moving an option moves its row instead of rewriting two of
                 them — which is what keeps the keyboard focus on the button
                 that was just pressed, and lets it be pressed again. */
              <div className={styles.option} key={`${field.key}-${option.value}`}>
                {/* The marker the applicant will meet, inert. It is the
                    quickest way to tell a Choice question from a Checkboxes
                    one without reading the type control at the top of the
                    card. A dropdown has no marker, so it gets none. */}
                {field.type === "select" ? null : (
                  <span
                    className={
                      field.type === "radio"
                        ? styles.optionMarkRound
                        : styles.optionMark
                    }
                    aria-hidden="true"
                  />
                )}

                {/* Only the label is editable. The value is what an answer is
                    stored as, and deriving it from the label would mean
                    rewording an option later silently changed what past
                    applicants appear to have said. It is shown beside the
                    label rather than hidden, because it is also the value that
                    turns up in an export. */}
                <input
                  aria-label={`Option ${at + 1}`}
                  className={styles.optionLabel}
                  value={option.label}
                  disabled={frozen}
                  onChange={(event) => setOption(at, { label: event.target.value })}
                />
                <code className={styles.optionValue}>{option.value}</code>

                <button
                  type="button"
                  className={styles.iconBtn}
                  aria-label={`Move option ${at + 1} up`}
                  disabled={frozen || at === 0}
                  onClick={() => moveOption(at, -1)}
                >
                  <ArrowUp />
                </button>
                <button
                  type="button"
                  className={styles.iconBtn}
                  aria-label={`Move option ${at + 1} down`}
                  disabled={frozen || at === field.options.length - 1}
                  onClick={() => moveOption(at, 1)}
                >
                  <ArrowDown />
                </button>
                <button
                  type="button"
                  className={`${styles.iconBtn} ${styles.iconDanger}`}
                  aria-label={`Remove option ${at + 1}`}
                  disabled={frozen || field.options.length === 1}
                  onClick={() =>
                    onChange({ options: field.options.filter((_, i) => i !== at) })
                  }
                >
                  <Cross />
                </button>
              </div>
            ))}
          </div>

          {frozen ? null : (
            <button
              type="button"
              className={styles.addOption}
              onClick={() =>
                onChange({
                  options: [
                    ...field.options,
                    {
                      value: nextOptionValue(field.options),
                      label: `Option ${field.options.length + 1}`,
                    },
                  ],
                })
              }
            >
              <Plus />
              Add option
            </button>
          )}
        </div>
      ) : null}

      <div className={styles.foot}>
        <label className={styles.check}>
          <input
            type="checkbox"
            checked={field.required}
            disabled={frozen}
            onChange={(event) => onChange({ required: event.target.checked })}
          />
          Required
        </label>
      </div>

      <Problems problems={problems} />
    </li>
  );
}

/**
 * A page break, open for editing.
 *
 * Drawn as a divider rather than as a card, because that is what it is: it does
 * not ask anything, and a form where the dividers and the questions look alike
 * is one where an author cannot see the shape of their own pages. The controls
 * are the two that make sense on one — where it sits, and whether it stays.
 *
 * No required toggle and no options editor, and not because they would be
 * ignored. The API refuses to publish a page break carrying either, so
 * offering them here would be offering a way to build a form that cannot go
 * live.
 */
function PageBreak({
  field,
  index,
  count,
  problems,
  disabled,
  settling,
  onChange,
  onMove,
  onRemove,
}: {
  field: FormField;
  index: number;
  count: number;
  problems: string[];
  disabled: boolean;
  settling: boolean;
  onChange: (changes: Partial<FormField>) => void;
  onMove: (delta: number) => void;
  onRemove: () => void;
}) {
  return (
    <li className={cardClass(styles.break, problems.length > 0, settling)}>
      <div className={styles.cardHead}>
        <span className={styles.breakIcon}>
          <PageBreakIcon />
        </span>
        <span className="pill lapsed">Page break</span>

        <span className={styles.spacer} />

        <div className={styles.tools}>
          <button
            type="button"
            className={styles.iconBtn}
            aria-label="Move up"
            disabled={disabled || index === 0}
            onClick={() => onMove(-1)}
          >
            <ArrowUp />
          </button>
          <button
            type="button"
            className={styles.iconBtn}
            aria-label="Move down"
            disabled={disabled || index === count - 1}
            onClick={() => onMove(1)}
          >
            <ArrowDown />
          </button>

          {/* No duplicate. A copy of a page break is an empty page immediately
              after this one, which is never what somebody meant to press. */}
          <button
            type="button"
            className={`${styles.iconBtn} ${styles.iconDanger}`}
            aria-label="Delete page break"
            disabled={disabled}
            onClick={onRemove}
          >
            <Trash />
          </button>
        </div>
      </div>

      <div className={styles.fields}>
        <div className={styles.field}>
          <label htmlFor={`${field.key}-label`}>Page heading</label>
          <input
            id={`${field.key}-label`}
            value={field.label}
            disabled={disabled}
            placeholder="What this page covers"
            onChange={(event) => onChange({ label: event.target.value })}
          />
        </div>

        <div className={styles.field}>
          <label htmlFor={`${field.key}-help`}>Description</label>
          <input
            id={`${field.key}-help`}
            value={field.help ?? ""}
            disabled={disabled}
            placeholder="Optional. Shown under the heading."
            onChange={(event) =>
              onChange({ help: event.target.value === "" ? null : event.target.value })
            }
          />
        </div>
      </div>

      <p className={styles.breakNote}>Everything below this is on a new page.</p>

      <Problems problems={problems} />
    </li>
  );
}

/**
 * What the API said is wrong with this one, under it.
 *
 * Beside the question rather than gathered at the top of the screen. Publishing
 * reports every problem at once, and a list of eleven complaints above a form
 * of twenty questions is eleven searches — the complaint has to be where the
 * thing it is about is.
 */
function Problems({ problems }: { problems: string[] }) {
  if (problems.length === 0) {
    return null;
  }

  return (
    <ul className={styles.problems}>
      {problems.map((problem) => (
        <li key={problem}>
          <Warning />
          {problem}
        </li>
      ))}
    </ul>
  );
}

/** The card, plus whatever is currently true of it. */
function cardClass(base: string, flagged: boolean, settling: boolean): string {
  return [base, flagged ? styles.flagged : null, settling ? styles.settling : null]
    .filter(Boolean)
    .join(" ");
}
