"use client";

import type { FieldOption, FormField } from "@/lib/api";
import { CHOICE_TYPES, TYPES, TYPE_NAMES, nextOptionValue } from "./fields";

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
  count,
  problems,
  disabled,
  onChange,
  onMove,
  onRemove,
}: {
  field: FormField;
  index: number;
  count: number;
  problems: string[];
  disabled: boolean;
  onChange: (changes: Partial<FormField>) => void;
  onMove: (delta: number) => void;
  onRemove: () => void;
}) {
  // Locked is what MLH's affiliation requires; disabled is not holding
  // forms.manage. They look the same to a hand on a keyboard and read very
  // differently, so only one of them gets an explanation.
  const frozen = field.locked || disabled;
  const choices = CHOICE_TYPES.has(field.type);

  const setOption = (at: number, changes: Partial<FieldOption>) =>
    onChange({
      options: field.options.map((option, i) =>
        i === at ? { ...option, ...changes } : option,
      ),
    });

  return (
    <li className={problems.length > 0 ? "question flagged" : "question"}>
      <div className="question-bar">
        <span className="ordinal">{index + 1}</span>

        {field.locked ? (
          <span className="pill lapsed">{TYPE_NAMES.get(field.type)}</span>
        ) : (
          <select
            aria-label="Question type"
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

        <span className="grow" />

        {/* Up and down rather than dragging. Dragging needs a library, and
            these lists are twelve questions long — two buttons are faster to
            aim at than a drop target and work from a keyboard. */}
        <button
          type="button"
          className="icon"
          aria-label="Move up"
          disabled={disabled || index === 0}
          onClick={() => onMove(-1)}
        >
          ↑
        </button>
        <button
          type="button"
          className="icon"
          aria-label="Move down"
          disabled={disabled || index === count - 1}
          onClick={() => onMove(1)}
        >
          ↓
        </button>

        {field.locked ? (
          <span className="pill sensitive" title="Required by MLH affiliation">
            Locked
          </span>
        ) : (
          <button
            type="button"
            className="icon danger"
            aria-label="Delete question"
            disabled={disabled}
            onClick={onRemove}
          >
            ✕
          </button>
        )}
      </div>

      {field.locked ? (
        <>
          <p className="locked-label">{field.label}</p>
          {/* One short line rather than the whole reason. Ten locked
              questions sit together at the top of every application form, and
              the full explanation repeated ten times is a wall somebody
              scrolls past. It is stated once, above the list. */}
          <p className="meta">MLH&rsquo;s wording, and not ours to change.</p>
        </>
      ) : (
        <>
          <label htmlFor={`${field.key}-label`}>Question</label>
          <input
            id={`${field.key}-label`}
            value={field.label}
            disabled={disabled}
            placeholder="What are you asking?"
            onChange={(event) => onChange({ label: event.target.value })}
          />

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
        </>
      )}

      {choices ? (
        <div className="options">
          <label>Options</label>
          {field.options.map((option, at) => (
            <div className="option" key={`${field.key}-${at}`}>
              {/* Only the label is editable. The value is what an answer is
                  stored as, and deriving it from the label would mean
                  rewording an option later silently changed what past
                  applicants appear to have said. It is shown beside the label
                  rather than hidden, because it is also the value that turns
                  up in an export. */}
              <input
                aria-label={`Option ${at + 1}`}
                value={option.label}
                disabled={frozen}
                onChange={(event) => setOption(at, { label: event.target.value })}
              />
              <code className="option-value">{option.value}</code>
              <button
                type="button"
                className="icon danger"
                aria-label={`Remove option ${at + 1}`}
                disabled={frozen || field.options.length === 1}
                onClick={() =>
                  onChange({ options: field.options.filter((_, i) => i !== at) })
                }
              >
                ✕
              </button>
            </div>
          ))}

          {frozen ? null : (
            <button
              type="button"
              className="link add-option"
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
              Add option
            </button>
          )}
        </div>
      ) : null}

      <div className="question-foot">
        <label className="check">
          <input
            type="checkbox"
            checked={field.required}
            disabled={frozen}
            onChange={(event) => onChange({ required: event.target.checked })}
          />
          Required
        </label>

        {/* Shown because it is the column header in an export and the property
            name in the stored answer, and somebody will eventually need to
            know it. Never editable: it is what an answer is filed under. */}
        <code className="meta" title="Answers are stored under this key">
          {field.key}
        </code>
      </div>

      {problems.length > 0 ? (
        <ul className="problems">
          {problems.map((problem) => (
            <li key={problem}>{problem}</li>
          ))}
        </ul>
      ) : null}
    </li>
  );
}
