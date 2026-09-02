"use client";

import { useRef, useState } from "react";
import { useRouter } from "next/navigation";
import type { Field } from "@/lib/api";
import { ResumeField, type Resume } from "./resume";

/** What one question's answer looks like while it is being filled in. */
type Answer = string | string[] | boolean | Resume;

type Answers = Record<string, Answer | undefined>;
type Problems = Record<string, string>;

/**
 * The form, and everything that happens when somebody submits it.
 *
 * The checks in here are a courtesy, not the rule. They save a round trip and
 * put each message next to the box it belongs to, which on a phone is the
 * difference between fixing one field and scrolling a long page hunting for
 * it. The API validates the same things against the published version, and
 * that is the check that decides anything — this file is deleteable in the
 * sense that removing it would make the form worse, not unsafe.
 */
export function Questions({ code, fields }: { code: string; fields: Field[] }) {
  const router = useRouter();
  const form = useRef<HTMLFormElement>(null);

  const [answers, setAnswers] = useState<Answers>({});
  const [problems, setProblems] = useState<Problems>({});
  const [banner, setBanner] = useState<string | null>(null);
  const [sending, setSending] = useState(false);

  /*
   * Which questions are still uploading something.
   *
   * A set rather than a boolean because a form is allowed one file question
   * today and this should not have to change on the day it is allowed two.
   * Submitting while a file is in flight would send an application with no
   * resume on it and no sign that anything was lost.
   */
  const [uploading, setUploading] = useState<string[]>([]);
  const busy = uploading.length > 0;

  function setBusy(key: string, isBusy: boolean) {
    setUploading((current) =>
      isBusy
        ? current.includes(key)
          ? current
          : [...current, key]
        : current.filter((k) => k !== key),
    );
  }

  function set(key: string, value: Answer | undefined) {
    setAnswers((current) => ({ ...current, [key]: value }));

    // The message goes the moment they start fixing it. Leaving it up while
    // somebody types reads as the fix not having worked.
    setProblems((current) => {
      if (!(key in current)) {
        return current;
      }

      const { [key]: _gone, ...rest } = current;
      return rest;
    });
  }

  /**
   * Puts the cursor on the first thing that is wrong.
   *
   * On a phone the failing question is usually off-screen, and a form that
   * refuses to submit without visibly saying why is one people give up on.
   */
  function goTo(key: string) {
    const field = form.current?.querySelector<HTMLElement>(`[data-key="${key}"]`);
    field?.scrollIntoView({ block: "center", behavior: "smooth" });
    field?.querySelector<HTMLElement>("input, select, textarea")?.focus();
  }

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (sending || busy) {
      return;
    }

    const found: Problems = {};
    for (const field of fields) {
      const problem = check(field, answers[field.key]);
      if (problem) {
        found[field.key] = problem;
      }
    }

    setProblems(found);
    const first = fields.find((f) => f.key in found);
    if (first) {
      setBanner(null);
      goTo(first.key);
      return;
    }

    setSending(true);
    setBanner(null);

    try {
      // Same origin, through the rewrite in next.config.ts. No CORS, no
      // preflight, and harbor is never a hostname this page has to know.
      const response = await fetch(
        `/api/forms/${encodeURIComponent(code)}/submit`,
        {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ answers: payload(fields, answers) }),
        },
      );

      if (response.ok) {
        // Replaced rather than pushed. Going Back to a form that has already
        // been accepted can only end in a confusing "you have already
        // applied", and there is nothing useful to do with it a second time.
        router.replace(`/${encodeURIComponent(code)}/thanks`);
        return;
      }

      const body = (await response.json().catch(() => ({}))) as {
        error?: string;
        problems?: { field: string | null; message: string }[];
      };

      // The API's own wording, not ours. It is the side that knows whether the
      // address was taken, the deadline passed, or an option is not on the
      // form, and a second copy of that knowledge over here would be a worse
      // one that drifts.
      const returned: Problems = {};
      for (const problem of body.problems ?? []) {
        if (problem.field) {
          returned[problem.field] = problem.message;
        }
      }

      setProblems(returned);

      const firstReturned = fields.find((f) => f.key in returned);
      if (firstReturned) {
        setBanner(null);
        goTo(firstReturned.key);
      } else {
        setBanner(body.error ?? "That did not go through. Try again.");
      }
    } catch {
      setBanner("We could not reach the server. Check your connection and try again.");
    } finally {
      setSending(false);
    }
  }

  return (
    <form ref={form} onSubmit={submit} noValidate>
      {banner ? (
        <div className="banner" role="alert">
          <p>{banner}</p>
        </div>
      ) : null}

      {fields.map((field) => (
        <Question
          key={field.key}
          code={code}
          field={field}
          answer={answers[field.key]}
          problem={problems[field.key]}
          onChange={set}
          onBusy={setBusy}
        />
      ))}

      <div className="submit">
        <button type="submit" disabled={sending || busy}>
          {busy ? "Waiting for your file…" : sending ? "Sending…" : "Submit"}
        </button>
        <p className="footnote">
          {busy
            ? "Your file is still uploading. Submitting now would leave it behind."
            : "You can only submit this once."}
        </p>
      </div>
    </form>
  );
}

/**
 * One question.
 *
 * Every type in the builder is rendered here and nowhere else, so adding one
 * is a single place to change — and a type that arrives without a case falls
 * through to a text box rather than to nothing, because a question an
 * applicant cannot answer is worse than a plain one.
 */
function Question({
  code,
  field,
  answer,
  problem,
  onChange,
  onBusy,
}: {
  code: string;
  field: Field;
  answer: Answer | undefined;
  problem: string | undefined;
  onChange: (key: string, value: Answer | undefined) => void;
  onBusy: (key: string, busy: boolean) => void;
}) {
  const id = `q-${field.key}`;
  const helpId = field.help ? `${id}-help` : undefined;
  const problemId = problem ? `${id}-problem` : undefined;
  const describedBy = [helpId, problemId].filter(Boolean).join(" ") || undefined;

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
        describedBy={describedBy}
        wrong={Boolean(problem)}
        onChange={onChange}
        onBusy={onBusy}
      />

      {problem ? (
        <strong className="wrong-note" id={problemId} role="alert">
          {problem}
        </strong>
      ) : null}
    </>
  );

  // A group of radios or checkboxes needs a fieldset and a legend, or a screen
  // reader announces each option with no idea what the question was.
  return grouped ? (
    <fieldset
      className={`question${problem ? " wrong" : ""}`}
      data-key={field.key}
    >
      <legend>
        {field.type === "consent" ? "Agreement" : field.label}
        <Requiredness field={field} />
      </legend>
      {body}
    </fieldset>
  ) : (
    <div className={`question${problem ? " wrong" : ""}`} data-key={field.key}>
      <label className="prompt" htmlFor={id}>
        {field.label}
        <Requiredness field={field} />
      </label>
      {body}
    </div>
  );
}

/**
 * Whether an answer is needed.
 *
 * A word rather than a red asterisk. Being required is not an error, and on a
 * page where --stop means "something went wrong" it must not look like one.
 * Optional questions are marked too, because on a long form the useful thing
 * to know is which ones can be skipped.
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
          value={text}
          maxLength={field.maxLength ?? undefined}
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
          inputMode="numeric"
          min={field.min ?? undefined}
          max={field.max ?? undefined}
          value={text}
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
        <>
          {field.options.map((option, index) => (
            <label className="choice" key={option.value}>
              <input
                type="radio"
                id={index === 0 ? id : undefined}
                name={field.key}
                value={option.value}
                checked={text === option.value}
                aria-describedby={describedBy}
                onChange={() => onChange(field.key, option.value)}
              />
              <span>{option.label}</span>
            </label>
          ))}
        </>
      );

    case "checkboxes": {
      const chosen = Array.isArray(answer) ? answer : [];

      return (
        <>
          {field.options.map((option, index) => (
            <label className="choice" key={option.value}>
              <input
                type="checkbox"
                id={index === 0 ? id : undefined}
                name={field.key}
                value={option.value}
                checked={chosen.includes(option.value)}
                aria-describedby={describedBy}
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
        </>
      );
    }

    case "consent":
      return (
        <label className="choice agreement">
          <input
            {...shared}
            type="checkbox"
            checked={answer === true}
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
          maxLength={field.maxLength ?? undefined}
          value={text}
          onChange={(e) => onChange(field.key, e.target.value)}
        />
      );
  }
}

/** Whether there is an answer here at all. Blank is absent, not empty. */
function answered(field: Field, answer: Answer | undefined): boolean {
  if (answer === undefined || answer === null) {
    return false;
  }

  if (field.type === "consent") {
    return answer === true;
  }

  if (Array.isArray(answer)) {
    return answer.length > 0;
  }

  // A file is only an answer once its bytes are somewhere. A picked file whose
  // upload failed is not one — reading it as an answer is how a required
  // resume question passes with nothing behind it.
  if (typeof answer === "object") {
    return answer.upload.length > 0;
  }

  return String(answer).trim().length > 0;
}

/**
 * The same rules the API applies, checked early.
 *
 * Kept short deliberately. Anything subtle enough that the two copies could
 * drift belongs on the server alone — a message that appears here and not
 * there is confusing, and one that appears there and not here is merely a
 * round trip.
 */
function check(field: Field, answer: Answer | undefined): string | null {
  if (!answered(field, answer)) {
    if (!field.required) {
      return null;
    }

    return field.type === "consent"
      ? "You have to agree to this to continue."
      : "This one is needed.";
  }

  if (typeof answer !== "string") {
    return null;
  }

  const value = answer.trim();

  if (field.type === "email" && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) {
    return "That does not look like an email address.";
  }

  if (field.type === "number") {
    const number = Number(value);

    if (!Number.isFinite(number)) {
      return "This has to be a number.";
    }

    if (field.min !== null && number < field.min) {
      return `This cannot be below ${field.min}.`;
    }

    if (field.max !== null && number > field.max) {
      return `This cannot be above ${field.max}.`;
    }
  }

  if (field.minLength !== null && value.length < field.minLength) {
    return `Needs at least ${field.minLength} characters.`;
  }

  if (field.maxLength !== null && value.length > field.maxLength) {
    return `Has to be under ${field.maxLength} characters.`;
  }

  return null;
}

/**
 * What actually gets posted.
 *
 * Only the questions the form asked, and only the ones with something in
 * them. The API ignores anything else anyway — it validates against the
 * version it loaded, not against this list — so this is about sending a clean
 * body rather than about safety.
 */
function payload(fields: Field[], answers: Answers): Record<string, unknown> {
  const body: Record<string, unknown> = {};

  for (const field of fields) {
    const answer = answers[field.key];
    if (!answered(field, answer)) {
      continue;
    }

    if (typeof answer === "string") {
      body[field.key] = answer.trim();
    } else if (typeof answer === "object" && !Array.isArray(answer)) {
      // The upload id and nothing else. The name and the size are held here
      // to draw the row that says what is attached; the API took both from
      // the file while it had it, and sending our copies back would be us
      // describing a file it is already looking at.
      body[field.key] = { upload: answer.upload };
    } else {
      body[field.key] = answer;
    }
  }

  return body;
}
