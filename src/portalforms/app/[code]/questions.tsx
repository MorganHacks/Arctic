"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import type { Field } from "@/lib/api";
import {
  answered,
  check,
  payload,
  shorten,
  type Answer,
  type Answers,
  type Problems,
} from "./answers";
import { Question, fieldId } from "./field";

/**
 * A form long enough that somebody wants to know how far through it they are.
 *
 * Below this the count is furniture: a five-question survey is a screen and a
 * half, and telling somebody they are two questions into it says nothing they
 * cannot see.
 */
const LONG_FORM = 8;

/**
 * The form, and everything that happens when somebody submits it.
 *
 * The checks in here are a courtesy, not the rule. They save a round trip and
 * put each message next to the box it belongs to, which on a phone is the
 * difference between fixing one field and scrolling a long page hunting for it.
 * The API validates the same things against the published version, and that is
 * the check that decides anything — this file is deleteable in the sense that
 * removing it would make the form worse, not unsafe.
 *
 * Nothing here ever clears an answer. Every failure path — a refused
 * submission, a dropped connection, a file that would not upload — leaves what
 * somebody typed exactly where they typed it, because the one unforgivable
 * thing a form can do is make a person write it out twice.
 */
export function Questions({ code, fields }: { code: string; fields: Field[] }) {
  const router = useRouter();
  const form = useRef<HTMLFormElement>(null);
  const summary = useRef<HTMLDivElement>(null);

  const [answers, setAnswers] = useState<Answers>({});
  const [problems, setProblems] = useState<Problems>({});
  const [banner, setBanner] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);

  /*
   * Bumped every time a submission is refused.
   *
   * The summary has to take focus on each attempt, including the second one
   * that fails the same way. Watching the problems themselves would move focus
   * on the first refusal and then sit silent while somebody pressed Submit
   * again and nothing appeared to happen.
   */
  const [attempt, setAttempt] = useState(0);

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

  const listed = useMemo(
    () => fields.filter((field) => field.key in problems),
    [fields, problems],
  );

  const done = fields.filter((field) => answered(field, answers[field.key])).length;
  const started = done > 0 || Object.keys(answers).length > 0;

  /*
   * A part-filled form is worth a browser's own "are you sure" and nothing
   * more. There is no server-side draft yet — nothing here knows who is coming
   * back — so a closed tab is genuinely lost, and the one protection available
   * is the one the browser already offers.
   *
   * The wording is the browser's, deliberately. Every browser ignores whatever
   * a page tries to put in it.
   */
  useEffect(() => {
    if (!started || sent) {
      return;
    }

    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [started, sent]);

  // Focus goes to the list of problems rather than to the first of them. Six
  // complaints and a cursor in the first box reads as one complaint; the list
  // is the only thing that says how much is left to fix.
  useEffect(() => {
    if (attempt > 0) {
      summary.current?.focus();
    }
  }, [attempt]);

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
   * Puts the cursor on one question.
   *
   * On a phone the failing question is usually off-screen, and a form that
   * refuses to submit without visibly saying why is one people give up on.
   */
  function goTo(key: string) {
    const question = form.current?.querySelector<HTMLElement>(
      `[data-key="${CSS.escape(key)}"]`,
    );

    const still = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    question?.scrollIntoView({ block: "start", behavior: still ? "auto" : "smooth" });
    question?.querySelector<HTMLElement>("input, select, textarea")?.focus();
  }

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (sending || busy) {
      return;
    }

    // Every question, every time. Stopping at the first would send somebody
    // back down the form once per mistake, and on a phone each trip is a
    // scroll through everything they already got right.
    const found: Problems = {};
    for (const field of fields) {
      const problem = check(field, answers[field.key]);
      if (problem) {
        found[field.key] = problem;
      }
    }

    if (Object.keys(found).length > 0) {
      setProblems(found);
      setBanner(null);
      setAttempt((n) => n + 1);
      return;
    }

    setProblems({});
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
        // Set before navigating, so the unload guard does not ask somebody
        // whether they meant to leave a form they have just submitted.
        setSent(true);

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
      let loose = false;

      for (const problem of body.problems ?? []) {
        if (problem.field) {
          returned[problem.field] = problem.message;
        } else {
          loose = true;
        }
      }

      setProblems(returned);

      // The banner and the list are not alternatives. A refusal can carry both
      // a sentence about the whole submission and a complaint about one
      // question, and dropping either leaves somebody without the half that
      // tells them what to do.
      const keyed = Object.keys(returned).length > 0;
      setBanner(keyed && !loose ? null : (body.error ?? "That did not go through. Try again."));

      setAttempt((n) => n + 1);
    } catch {
      setBanner("We could not reach the server. Check your connection and try again.");
      setAttempt((n) => n + 1);
    } finally {
      setSending(false);
    }
  }

  return (
    <form ref={form} onSubmit={submit} noValidate aria-busy={sending || undefined}>
      {fields.length >= LONG_FORM ? (
        <Progress done={done} total={fields.length} />
      ) : null}

      {/*
       * One place that says everything that is wrong, at the top, taking focus
       * when a submission is refused. It is the pattern a screen reader user
       * expects and the only one that answers "how much is left" — a cursor
       * dropped in the first bad box answers "what is wrong here" and nothing
       * else.
       */}
      <div
        className="summary"
        ref={summary}
        tabIndex={-1}
        role="alert"
        aria-live="assertive"
      >
        {banner ? <p className="summary-lede">{banner}</p> : null}

        {listed.length > 0 ? (
          <>
            <p className="summary-lede">Some answers need another look.</p>
            <ul>
              {listed.map((field) => (
                <li key={field.key}>
                  <a
                    href={`#${fieldId(field.key)}`}
                    onClick={(e) => {
                      e.preventDefault();
                      goTo(field.key);
                    }}
                  >
                    {shorten(field.label)}
                  </a>{" "}
                  — {problems[field.key]}
                </li>
              ))}
            </ul>
          </>
        ) : null}
      </div>

      {fields.map((field, index) => (
        <Question
          key={field.key}
          code={code}
          field={field}
          index={index + 1}
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
 * How far through the form somebody is.
 *
 * A count and a bar, not a percentage on its own. On a thirty-question form the
 * useful number is how many questions are left, and only a count answers that.
 *
 * The bar is decoration over the number and is hidden from a screen reader
 * accordingly. The count is not a live region: it changes on every keystroke,
 * and announcing it would talk over the box somebody is typing into.
 */
function Progress({ done, total }: { done: number; total: number }) {
  return (
    <div className="tally">
      <p className="tally-count">
        {done} of {total} answered
      </p>
      <div className="tally-track" aria-hidden="true">
        <div
          className="tally-bar"
          style={{ width: `${total > 0 ? (done / total) * 100 : 0}%` }}
        />
      </div>
    </div>
  );
}
