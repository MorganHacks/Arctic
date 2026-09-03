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
import { plan } from "./steps";

/**
 * A form long enough that somebody wants to know how far through it they are.
 *
 * Below this the count is furniture: a five-question survey is a screen and a
 * half, and telling somebody they are two questions into it says nothing they
 * cannot see.
 *
 * Only consulted for a form that is one step. A form in steps always says which
 * step it is on, however short it is, because the questions somebody cannot see
 * are the ones they most need counting.
 */
const LONG_FORM = 8;

/**
 * Where focus goes after something moves.
 *
 * The counter is what makes a repeat land: the summary has to take focus on
 * every refused attempt, including the second one that fails in exactly the
 * same way. Watching the problems themselves would move focus on the first
 * refusal and then sit silent while somebody pressed the button again and
 * nothing appeared to happen.
 */
type Landing = { at: "summary" | "step"; n: number };

/**
 * The form, and everything that happens when somebody moves through it.
 *
 * The checks in here are a courtesy, not the rule. They save a round trip and
 * put each message next to the box it belongs to, which on a phone is the
 * difference between fixing one field and scrolling a long page hunting for it.
 * The API validates the same things against the published version, and that is
 * the check that decides anything — this file is deleteable in the sense that
 * removing it would make the form worse, not unsafe.
 *
 * Nothing here ever clears an answer. Every failure path — a refused
 * submission, a dropped connection, a file that would not upload, a step
 * somebody walked away from and came back to — leaves what they typed exactly
 * where they typed it, because the one unforgivable thing a form can do is make
 * a person write it out twice.
 *
 * ## The browser's own Back button
 *
 * A step is deliberately **not** a history entry. Back leaves the form, and the
 * unload guard below makes the browser ask first.
 *
 * The alternative is tempting and was not taken. Pushing an entry per step
 * makes the browser's Back mean "previous step", which is what a phone user's
 * thumb expects — but every way of doing it hands the App Router a navigation,
 * and a navigation this component does not survive takes every answer with it.
 * `router.push` re-runs the server component, which loads the form `no-store`
 * and can remount this tree. Raw `pushState` avoids that until a `popstate`
 * arrives for an entry the router did not create, and what it does then is not
 * something this page should be betting somebody's application on.
 *
 * So: the on-screen Back button is how you go back a step, and the browser's
 * Back button is how you leave — with a confirmation in front of it. The worst
 * case is somebody swiping back out of habit and being asked whether they meant
 * it. The worst case of the other choice is a fifteen-question application
 * gone, silently, with nothing on screen to say why.
 */
export function Questions({ code, fields }: { code: string; fields: Field[] }) {
  const router = useRouter();
  const form = useRef<HTMLFormElement>(null);
  const summary = useRef<HTMLDivElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);

  const { steps, ordinals, questions } = useMemo(() => plan(fields), [fields]);

  const [answers, setAnswers] = useState<Answers>({});
  const [problems, setProblems] = useState<Problems>({});
  const [banner, setBanner] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);
  const [landing, setLanding] = useState<Landing | null>(null);

  /*
   * Which step is on screen.
   *
   * The only thing that changes when somebody presses Next. Every answer for
   * every step, reached or not, lives in `answers` above and is untouched by
   * moving — which is what makes going back and forward again free.
   */
  const [at, setAt] = useState(0);

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

  const step = steps[at];
  const last = at === steps.length - 1;
  const stepped = steps.length > 1;

  // Only what is on screen. A list of problems is a list of things to go and
  // fix, and a link to a question three steps away goes nowhere.
  const listed = useMemo(
    () => step.fields.filter((field) => field.key in problems),
    [step, problems],
  );

  const done = fields.filter(
    (field) => field.type !== "section" && answered(field, answers[field.key]),
  ).length;

  const started = done > 0 || Object.keys(answers).length > 0;

  /*
   * A part-filled form is worth a browser's own "are you sure" and nothing
   * more. There is no server-side draft yet — nothing here knows who is coming
   * back — so a closed tab is genuinely lost, and the one protection available
   * is the one the browser already offers.
   *
   * It matters more in steps than it did on one page. The browser's Back
   * button is now the only way to leave a form somebody is half way through,
   * and this is what stands between a habitual back-swipe and a lost
   * application.
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

  /*
   * One effect for the cursor, not two.
   *
   * A refused submission can raise problems *and* send somebody back a step,
   * and two effects racing over where focus lands is how it ends up somewhere
   * neither of them meant. So the handlers say where it goes and this puts it
   * there.
   *
   * Focus on the problems rather than on the first of them: six complaints and
   * a cursor in the first box reads as one complaint, and the list is the only
   * thing that says how much is left to fix.
   *
   * Focus on the heading when a step changes, which is also how the change is
   * announced. The heading reads "Step 2 of 4" and then the step's name, so a
   * screen reader says where somebody now is without a live region talking over
   * whatever they were doing. A silent swap of every question on the page is
   * the thing this is here to prevent.
   */
  useEffect(() => {
    if (!landing) {
      return;
    }

    if (landing.at === "summary") {
      summary.current?.focus();
      return;
    }

    // The top of the page, not the top of the step: the form's name is two
    // lines above and is worth seeing again after everything under it changed.
    window.scrollTo({ top: 0, behavior: still() ? "auto" : "smooth" });
    heading.current?.focus({ preventScroll: true });
  }, [landing]);

  function land(where: Landing["at"]) {
    setLanding((current) => ({ at: where, n: (current?.n ?? 0) + 1 }));
  }

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
   * refuses to move on without visibly saying why is one people give up on.
   */
  function goTo(key: string) {
    const question = form.current?.querySelector<HTMLElement>(
      `[data-key="${CSS.escape(key)}"]`,
    );

    question?.scrollIntoView({ block: "start", behavior: still() ? "auto" : "smooth" });
    question?.querySelector<HTMLElement>("input, select, textarea")?.focus();
  }

  /** Every problem on a set of questions, found in one pass. */
  function review(list: Field[]): Problems {
    const found: Problems = {};

    for (const field of list) {
      const problem = check(field, answers[field.key]);
      if (problem) {
        found[field.key] = problem;
      }
    }

    return found;
  }

  /** The first step holding any of these questions, or -1. */
  function stepWith(keys: string[]): number {
    const wanted = new Set(keys);
    return steps.findIndex((one) => one.fields.some((f) => wanted.has(f.key)));
  }

  /**
   * On to the next step, if this one is finished.
   *
   * Only this step is checked. Somebody on step one must not be told about a
   * required question on step four — it is not on their screen, there is
   * nothing they can do about it from here, and a Next button that refuses for
   * a reason nobody can see is a form that appears broken.
   *
   * Every problem on *this* step at once, though. Stopping at the first would
   * send somebody down the same step once per mistake.
   */
  function forward() {
    const found = review(step.fields);

    // Problems raised elsewhere are kept. The API can refuse a submission with
    // a complaint about a question two steps back, and that message has to
    // still be there when somebody walks back to it.
    setProblems((current) => {
      const kept: Problems = {};

      for (const [key, message] of Object.entries(current)) {
        if (!step.fields.some((field) => field.key === key)) {
          kept[key] = message;
        }
      }

      return { ...kept, ...found };
    });

    setBanner(null);

    if (Object.keys(found).length > 0) {
      land("summary");
      return;
    }

    setAt((n) => n + 1);
    land("step");
  }

  /**
   * Back a step.
   *
   * Never refused and never validated. Going backwards cannot make a form
   * worse, and a Back button that argues is one somebody has to fight to
   * re-read a question they have already answered.
   */
  function back() {
    setBanner(null);
    setAt((n) => Math.max(0, n - 1));
    land("step");
  }

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (sending || busy) {
      return;
    }

    // Every step but the last one only moves on. The same button, because
    // pressing Enter in a text box should do the obvious thing on every step.
    if (!last) {
      forward();
      return;
    }

    /*
     * The whole form, on the last step only.
     *
     * Everything behind here has already passed its own step's check, so this
     * usually finds nothing. It is here for the one path that gets past those:
     * somebody walking back and emptying a box they had already filled in.
     *
     * If it does find something, they are taken to the step it is on. A
     * complaint about a question somebody cannot see is not a complaint they
     * can act on.
     */
    const found = review(fields);

    if (Object.keys(found).length > 0) {
      setProblems(found);
      setBanner(null);

      const earliest = stepWith(Object.keys(found));
      if (earliest !== -1) {
        setAt(earliest);
      }

      land("summary");
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

      // Refused over a question three steps back, so that is where somebody
      // has to be standing to read about it. A problem with no question
      // attached leaves them here, on the last step, where the button they
      // pressed is.
      const earliest = stepWith(Object.keys(returned));
      if (earliest !== -1) {
        setAt(earliest);
      }

      // The banner and the list are not alternatives. A refusal can carry both
      // a sentence about the whole submission and a complaint about one
      // question, and dropping either leaves somebody without the half that
      // tells them what to do.
      const keyed = Object.keys(returned).length > 0;
      setBanner(keyed && !loose ? null : (body.error ?? "That did not go through. Try again."));

      land("summary");
    } catch {
      setBanner("We could not reach the server. Check your connection and try again.");
      land("summary");
    } finally {
      setSending(false);
    }
  }

  return (
    <form ref={form} onSubmit={submit} noValidate aria-busy={sending || undefined}>
      {stepped ? (
        <Step
          heading={heading}
          section={step.section}
          at={at}
          total={steps.length}
        />
      ) : questions >= LONG_FORM ? (
        <Progress done={done} total={questions} />
      ) : null}

      {/*
       * One place that says everything that is wrong, at the top, taking focus
       * when a step or a submission is refused. It is the pattern a screen
       * reader user expects and the only one that answers "how much is left" —
       * a cursor dropped in the first bad box answers "what is wrong here" and
       * nothing else.
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

      {/*
       * Only this step's questions are mounted. The answers are not in here —
       * they are in `answers`, one object for the whole form — so unmounting a
       * step costs nothing and remounting it puts every box back exactly as it
       * was left, including on a step somebody has not reached yet.
       *
       * The key is the step number, so React replaces this subtree when the
       * step changes and the arrival animation in the stylesheet plays again.
       * Nothing else re-keys it: typing an answer, raising a problem and
       * clearing one all leave `at` alone, so the questions do not flicker
       * every time somebody presses a key.
       */}
      <div className="step-body" key={at}>
        {step.fields.map((field) => (
          <Question
            key={field.key}
            code={code}
            field={field}
            index={ordinals[field.key]}
            answer={answers[field.key]}
            problem={problems[field.key]}
            onChange={set}
            onBusy={setBusy}
          />
        ))}
      </div>

      <div className="submit">
        {at > 0 ? (
          <button type="button" className="back" onClick={back}>
            Back
          </button>
        ) : null}

        <button type="submit" disabled={sending || busy}>
          {busy
            ? "Waiting for your file…"
            : sending
              ? "Sending…"
              : last
                ? "Submit"
                : "Next"}
        </button>

        <Footnote busy={busy} last={last} />
      </div>
    </form>
  );
}

/**
 * The line under the button.
 *
 * Says something only when there is something to say. On the last step that is
 * what pressing Submit commits to; while a file is going up it is why the
 * button will not move. On every other step it is silent, because a sentence
 * repeated on four screens in a row is one nobody reads by the second.
 */
function Footnote({ busy, last }: { busy: boolean; last: boolean }) {
  if (busy) {
    return (
      <p className="footnote">
        {last
          ? "Your file is still uploading. Submitting now would leave it behind."
          : "Your file is still uploading. Moving on now would leave it behind."}
      </p>
    );
  }

  return last ? <p className="footnote">You can only submit this once.</p> : null;
}

/**
 * Which step somebody is on, and what it is called.
 *
 * This is the progress indicator for a form in steps — there is not a second
 * one. The `n of m answered` count below is for a form that is one page, where
 * every question is on screen and the useful number is how many of them are
 * done. Once the form is in steps the useful number is which step, and running
 * both would put two different measures of "how far through" side by side.
 *
 * The count lives inside the heading rather than beside it, and the heading is
 * what takes focus when a step changes. That makes one announcement — "Step 2
 * of 4, About you" — out of what would otherwise be a live region competing
 * with whatever somebody was reading.
 *
 * The bar is decoration over the count and is hidden from a screen reader
 * accordingly. It is uncoloured for the same reason the other one is: on this
 * page colour means something went wrong or something needs doing, and progress
 * is neither.
 */
function Step({
  heading,
  section,
  at,
  total,
}: {
  heading: React.Ref<HTMLHeadingElement>;
  section: { label: string; help: string | null } | null;
  at: number;
  total: number;
}) {
  return (
    <div className="step">
      {/*
       * One segment per step, not a bar filled to a fraction.
       *
       * A form in steps has a small whole number of them, and the question
       * somebody standing on a pavement is asking is "how many more screens is
       * this" — which is answered by counting segments and not by judging a
       * proportion. Hidden from a screen reader because the heading below says
       * the same thing in words, and says it better.
       */}
      <div className="step-track" aria-hidden="true">
        {Array.from({ length: total }, (_, n) => (
          <span key={n} className={n <= at ? "on" : undefined} />
        ))}
      </div>

      <h2 className="step-title" ref={heading} tabIndex={-1}>
        <span className="step-of">
          Step {at + 1} of {total}
        </span>
        {/* An explicit space between the count and the name. The count is a
            block and sits on its own line, so this is invisible — but the
            heading is read as one string when it takes focus, and without it
            that string is "Step 2 of 4About you". */}
        {section ? <> {section.label}</> : null}

        {/* A step with no section is the run of questions before the first one.
            It has no name, and making one up would be writing copy the form's
            author did not. */}
      </h2>

      {section?.help ? <p className="step-help">{section.help}</p> : null}
    </div>
  );
}

/**
 * How far through a one-page form somebody is.
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

/** Whether this browser has been asked not to animate anything. */
function still(): boolean {
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}
