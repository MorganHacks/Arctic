"use client";

import type { Field } from "@/lib/api";
import { shownCap, type Answer } from "./answers";
import { ResumeField } from "./resume";

/**
 * One question.
 *
 * Every type the builder can produce is rendered here and nowhere else, so
 * adding one is a single place to change — and a type that arrives without a
 * case falls through to a text box rather than to nothing, because a question
 * an applicant cannot answer is worse than a plain one.
 *
 * The ids are wired by hand rather than left to the browser's guesswork. A
 * question is a label, sometimes a hint, sometimes a counter and sometimes a
 * complaint, and all four have to reach somebody who is hearing the page rather
 * than looking at it.
 */
export function Question({
  code,
  field,
  index,
  answer,
  problem,
  fixed,
  onChange,
  onBusy,
}: {
  code: string;
  field: Field;
  index: number;
  answer: Answer | undefined;
  problem: string | undefined;

  /**
   * Whether this is ours to state rather than theirs to answer.
   *
   * Only ever true on a form that required signing in, and only for identity
   * we verified and agreements already given. Rendered as the answer with no
   * control, never hidden: a question that vanishes is one somebody assumes
   * was never asked.
   *
   * A courtesy either way. The API replaces every one of these with what it
   * holds before it validates anything, so a crafted submission changes
   * nothing — this is what stops somebody trying.
   */
  fixed?: boolean;
  onChange: (key: string, value: Answer | undefined) => void;
  onBusy: (key: string, busy: boolean) => void;
}) {
  const id = fieldId(field.key);
  const helpId = field.help ? `${id}-help` : undefined;
  const problemId = problem ? `${id}-problem` : undefined;

  const cap = shownCap(field);
  const typed = typeof answer === "string" ? answer : "";
  const counterId = cap === null ? undefined : `${id}-count`;

  const describedBy =
    [helpId, counterId, problemId].filter(Boolean).join(" ") || undefined;

  /*
   * A fixed question is never a group and never a file.
   *
   * It has no control at all, so the fieldset and legend a radio group needs
   * would be a group of nothing — and the counter and the length cap below are
   * about a box somebody is typing into, which there is not one of.
   */
  if (fixed) {
    return <Held field={field} index={index} answer={answer} />;
  }

  const grouped =
    field.type === "radio" ||
    field.type === "checkboxes" ||
    field.type === "consent";

  const body = (
    <>
      {field.help ? (
        <p className="help" id={helpId}>
          {field.help}
        </p>
      ) : null}

      <Control
        code={code}
        field={field}
        id={id}
        answer={answer}
        describedBy={grouped ? undefined : describedBy}
        wrong={Boolean(problem)}
        onChange={onChange}
        onBusy={onBusy}
      />

      {cap === null ? null : (
        <p className="counter" id={counterId}>
          {/* Numerals rather than a sentence. This sits under a box somebody is
              typing into and has to be readable without being read. */}
          {typed.length} / {cap}
        </p>
      )}

      {problem ? (
        /*
         * Not a live region. On a form with six problems, six live regions
         * mounting at once is six interruptions in a row and none of them
         * says which question it belongs to. The summary at the top of the
         * form is the announcement; this is what somebody finds when they
         * get here.
         */
        <strong className="wrong-note" id={problemId}>
          {problem}
        </strong>
      ) : null}
    </>
  );

  // A group of radios or checkboxes needs a fieldset and a legend, or a screen
  // reader announces each option with no idea what the question was. The hint
  // and the complaint hang off the group rather than off each option, so they
  // are said once instead of once per choice.
  return grouped ? (
    <fieldset
      className={`question${problem ? " wrong" : ""}`}
      data-key={field.key}
      aria-describedby={describedBy}
      aria-invalid={problem ? true : undefined}
    >
      <legend>
        <span className="ordinal">{index}</span>
        {field.type === "consent" ? "Agreement" : field.label}
        <Requiredness field={field} />
      </legend>
      {body}
    </fieldset>
  ) : (
    <div className={`question${problem ? " wrong" : ""}`} data-key={field.key}>
      <label className="prompt" htmlFor={id}>
        <span className="ordinal">{index}</span>
        {field.label}
        <Requiredness field={field} />
      </label>
      {body}
    </div>
  );
}

/**
 * A question whose answer is already settled.
 *
 * The label, the answer, and one line saying it is not changed here. No
 * control, no `Required` marker — nothing is being asked — and no disabled
 * input, which reads as something that ought to work and does not.
 *
 * The wording of the note names no route to changing it, because there is not
 * one on this page and inventing one would be worse than saying nothing.
 */
function Held({
  field,
  index,
  answer,
}: {
  field: Field;
  index: number;
  answer: Answer | undefined;
}) {
  return (
    <div className="question" data-key={field.key}>
      <p className="prompt">
        <span className="ordinal">{index}</span>
        {field.type === "consent" ? "Agreement" : field.label}
      </p>

      {field.type === "consent" ? (
        <p>{field.label}</p>
      ) : (
        <p className="held">{written(answer)}</p>
      )}

      <p className="help">
        We already have this from your application, so it is not asked again
        here.
      </p>
    </div>
  );
}

/**
 * One held answer, as a line of text.
 *
 * A ticked agreement reads as "Agreed" rather than "true", which is the only
 * one of these that needs saying in words — the rest are already what somebody
 * wrote.
 */
function written(answer: Answer | undefined): string {
  if (answer === true) {
    return "Agreed";
  }

  if (Array.isArray(answer)) {
    return answer.join(", ");
  }

  return typeof answer === "string" ? answer : "";
}

/** The id of the control a question's problem should send somebody to. */
export function fieldId(key: string): string {
  return `q-${key}`;
}

/**
 * Whether an answer is needed.
 *
 * A word rather than a red asterisk. Being required is not an error, and on a
 * page where --stop means "something went wrong" it must not look like one.
 * Optional questions are marked too, because on a long form the useful thing to
 * know is which ones can be skipped.
 */
function Requiredness({ field }: { field: Field }) {
  return field.required ? (
    <span className="needed">Required</span>
  ) : (
    <span className="optional">Optional</span>
  );
}

function Control({
  code,
  field,
  id,
  answer,
  describedBy,
  wrong,
  onChange,
  onBusy,
}: {
  code: string;
  field: Field;
  id: string;
  answer: Answer | undefined;
  describedBy: string | undefined;
  wrong: boolean;
  onChange: (key: string, value: Answer | undefined) => void;
  onBusy: (key: string, busy: boolean) => void;
}) {
  const shared = {
    id,
    name: field.key,
    "aria-describedby": describedBy,
    "aria-invalid": wrong || undefined,
    className: wrong ? "wrong" : undefined,
  };

  const text = typeof answer === "string" ? answer : "";

  switch (field.type) {
    case "paragraph":
      return (
        <textarea
          {...shared}
          rows={4}
          value={text}
          onChange={(e) => onChange(field.key, e.target.value)}
        />
      );

    case "email":
      return (
        <input
          {...shared}
          type="email"
          inputMode="email"
          autoComplete="email"
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
          value={text}
          onChange={(e) => onChange(field.key, e.target.value)}
        />
      );

    case "phone":
      return (
        <input
          {...shared}
          type="tel"
          inputMode="tel"
          autoComplete="tel"
          value={text}
          onChange={(e) => onChange(field.key, e.target.value)}
        />
      );

    case "number":
      return (
        <input
          {...shared}
          type="number"
          inputMode="decimal"
          min={field.min ?? undefined}
          max={field.max ?? undefined}
          value={text}
          /*
           * A wheel over a focused number box changes the number. Somebody
           * scrolling a long form past a question they have already answered
           * would silently rewrite it, and nothing on the page would say so.
           */
          onWheel={(e) => e.currentTarget.blur()}
          onChange={(e) => onChange(field.key, e.target.value)}
        />
      );

    case "date":
      return (
        <input
          {...shared}
          type="date"
          value={text}
          onChange={(e) => onChange(field.key, e.target.value)}
        />
      );

    case "select":
      return (
        <select
          {...shared}
          value={text}
          onChange={(e) => onChange(field.key, e.target.value)}
        >
          {/* Empty and first, so an untouched dropdown does not silently
              answer with whichever option happened to be listed first. */}
          <option value="">Choose one…</option>
          {field.options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      );

    case "radio":
      return (
        <div className="choices">
          {field.options.map((option, index) => (
            <label className="choice" key={option.value}>
              <input
                type="radio"
                id={index === 0 ? id : undefined}
                name={field.key}
                value={option.value}
                checked={text === option.value}
                aria-invalid={wrong || undefined}
                onChange={() => onChange(field.key, option.value)}
              />
              <span>{option.label}</span>
            </label>
          ))}
        </div>
      );

    case "checkboxes": {
      const chosen = Array.isArray(answer) ? answer : [];

      return (
        <div className="choices">
          {field.options.map((option, index) => (
            <label className="choice" key={option.value}>
              <input
                type="checkbox"
                id={index === 0 ? id : undefined}
                name={field.key}
                value={option.value}
                checked={chosen.includes(option.value)}
                aria-invalid={wrong || undefined}
                onChange={(e) =>
                  onChange(
                    field.key,
                    e.target.checked
                      ? [...chosen, option.value]
                      : chosen.filter((v) => v !== option.value),
                  )
                }
              />
              <span>{option.label}</span>
            </label>
          ))}
        </div>
      );
    }

    case "consent":
      return (
        <label className="choice agreement">
          <input
            id={id}
            name={field.key}
            type="checkbox"
            checked={answer === true}
            aria-invalid={wrong || undefined}
            onChange={(e) => onChange(field.key, e.target.checked)}
          />
          {/* The wording is MLH's and is not ours to shorten. It sits beside
              the tick rather than above it so the two read as one act. */}
          <span>{field.label}</span>
        </label>
      );

    case "file":
      // The one control that talks to the API before the form is submitted,
      // so it owns its own state rather than being another branch here.
      return (
        <ResumeField
          field={field}
          id={id}
          code={code}
          describedBy={describedBy}
          wrong={wrong}
          value={
            typeof answer === "object" && !Array.isArray(answer)
              ? answer
              : undefined
          }
          onChange={onChange}
          onBusy={onBusy}
        />
      );

    default:
      return (
        <input
          {...shared}
          type="text"
          value={text}
          onChange={(e) => onChange(field.key, e.target.value)}
        />
      );
  }
}
