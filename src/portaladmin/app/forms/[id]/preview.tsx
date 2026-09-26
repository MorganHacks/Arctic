"use client";

import { summarizeErrors } from "../../../../../libs/ui/error-notifications";
import { ErrorDescription, ErrorToast } from "@/components/ui/error-toast";
import { FormLinkCard } from "@/components/ui/form-link-card";
import type { FormLinkCard as Card } from "../../../../../libs/ui/form-theme";
import { useId, useLayoutEffect, useRef, useState, type FormEvent } from "react";
import { Select } from "@/components/ui/select";
import { CheckmarkCircle02Icon, Link04Icon, Tick02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { EmptyState } from "@/components/ui/empty-state";
import { publicLink, useCopy } from "@/components/formslist/share-link";
import type { FormField } from "@/lib/api";
import { previewPages, previewProblem } from "./preview-answers";
import styles from "./builder.module.css";

export function PreviewActions({ code, published }: { code: string; published: boolean }) {
  const { state, copy } = useCopy(code);
  return (
    <div className={styles.previewActions}>
      <span className={styles.previewStatus} data-live={published}>
        {published ? <Icon icon={CheckmarkCircle02Icon} size={17} /> : null}
        {published ? "Published" : "Draft"}
      </span>
      <button type="button" className={styles.toolbarButton} onClick={copy}>
        <Icon icon={state === "copied" ? Tick02Icon : Link04Icon} size={16} />
        {state === "copied" ? "Link copied" : "Copy responder link"}
      </button>
      <span className={styles.dragInstructions} role="status">{state === "copied" ? "Link copied." : null}</span>
      <ErrorToast message={state === "failed" ? `Could not copy. The link is ${publicLink(code)}` : null} />
    </div>
  );
}

export function Preview({ fields, formName, headerImage, linkCard }: {
  fields: FormField[];
  formName: string;
  headerImage?: string | null;
  linkCard?: Card | null;
}) {
  const formId = useId();
  const panel = useRef<HTMLElement>(null);
  const form = useRef<HTMLFormElement>(null);
  const [page, setPage] = useState(0);
  const [problems, setProblems] = useState<Record<string, string>>({});
  const [submitted, setSubmitted] = useState(false);
  const [announcement, setAnnouncement] = useState("");
  const [focusRequest, setFocusRequest] = useState(0);
  const pages = previewPages(fields);
  const current = pages[page];
  const hasRequired = fields.some(field => field.type !== "section" && field.required);

  useLayoutEffect(() => {
    if (!form.current || !Object.keys(problems).length) return;
    const invalid = form.current.querySelector<HTMLElement>('[data-preview-page]:not([hidden]) [aria-invalid="true"]');
    if (invalid instanceof HTMLFieldSetElement) invalid.querySelector<HTMLElement>("input")?.focus();
    else invalid?.focus();
  }, [focusRequest]);

  function moveTo(next: number) {
    setPage(next);
    setAnnouncement("");
    panel.current?.scrollIntoView({ block: "start" });
  }

  function problemFor(field: FormField, data: FormData) {
    const input = form.current?.elements.namedItem(field.key);
    if (input instanceof HTMLInputElement && input.validity.badInput) return field.type === "number" ? "Enter a number." : "Enter a valid value.";
    return previewProblem(field, data);
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const last = page === pages.length - 1;
    const checked = last ? fields : current.fields;
    const nextProblems: Record<string, string> = {};
    for (const field of checked) {
      const problem = problemFor(field, data);
      if (problem) nextProblems[field.key] = problem;
    }
    setProblems(nextProblems);
    const count = Object.keys(nextProblems).length;
    if (count) {
      const firstPage = pages.findIndex(step => step.fields.some(field => nextProblems[field.key]));
      setPage(firstPage);
      setFocusRequest(request => request + 1);
      setAnnouncement(`${count} ${count === 1 ? "question needs" : "questions need"} your attention.`);
    } else if (!last) {
      moveTo(page + 1);
    } else {
      setSubmitted(true);
      setAnnouncement("Preview complete. Your test answers have not been submitted.");
      panel.current?.scrollIntoView({ block: "start" });
    }
  }

  return (
    <section ref={panel} className={`${styles.previewPanel} ${styles.preview}`} aria-label="Form preview" data-link-card={!!linkCard}>
      {headerImage ? <img className={styles.previewHeaderImage} src={headerImage} alt="Form header" /> : null}
      <div className={styles.previewIntro}>
        <div className={styles.previewRule} />
        <div className={styles.previewTop}>
          <h1 className={styles.previewTitle}>{formName}</h1>
          {current.section?.label ? <p className={styles.pageHeading}>{current.section.label}</p> : null}
          {current.section?.help ? <p className={styles.help}>{current.section.help}</p> : null}
        </div>
        {hasRequired && !submitted ? <p className={styles.previewRequired}><span>*</span> Indicates a required question</p> : null}
      </div>
      <span className={styles.dragInstructions} role="status">{Object.keys(problems).length ? "" : announcement}</span>
      <ErrorToast title="Check your answers" revision={focusRequest} message={summarizeErrors(Object.entries(problems).map(([key, problem]) => `${fields.find(field => field.key === key)?.label || "Question"}: ${problem}`))} />
      {submitted ? (
        <div className={styles.previewComplete}>
          <Icon icon={CheckmarkCircle02Icon} size={28} />
          <h2>Preview complete</h2>
          <p>Your test answers have not been submitted.</p>
          <button type="button" className={styles.toolbarButton} onClick={() => { setSubmitted(false); setAnnouncement(""); }}>Back to preview</button>
        </div>
      ) : null}
      <form ref={form} id={formId} className={styles.previewBody} hidden={submitted} autoComplete="off" noValidate onSubmit={submit}
        onChange={event => {
          const target = event.target;
          if (!(target instanceof HTMLInputElement || target instanceof HTMLSelectElement || target instanceof HTMLTextAreaElement) || !problems[target.name]) return;
          const field = fields.find(item => item.key === target.name);
          if (!field) return;
          const problem = problemFor(field, new FormData(event.currentTarget));
          setProblems(previous => {
            const next = { ...previous };
            if (problem) next[field.key] = problem;
            else delete next[field.key];
            return next;
          });
        }}>
        {fields.length === 0 ? (
          <div className={styles.asked}><EmptyState size="compact" title="Your form starts here" description="Add a question to see how it will look." /></div>
        ) : (
          <>
            {pages.map((step, index) => (
              <div key={step.section?.key ?? "first"} className={styles.previewQuestions} data-preview-page={index} hidden={page !== index}>
                {step.fields.map(field => <Asked key={field.key} field={field} problem={problems[field.key]} />)}
              </div>
            ))}
            <div className={styles.previewFooter}>
              <div className={styles.previewButtons}>
                {page > 0 ? <button type="button" className={styles.previewPrevious} onClick={() => moveTo(page - 1)}>Back</button> : null}
                <button type="submit" className={styles.previewSubmit}>{page === pages.length - 1 ? "Submit" : "Next"}</button>
              </div>
              <div className={styles.previewProgress}>
                <progress value={page + 1} max={pages.length} aria-label="Form progress" />
                <span>Page {page + 1} of {pages.length}</span>
              </div>
            </div>
            <p className={styles.previewNote}>This is a preview. Test answers won’t be submitted.</p>
          </>
        )}
      </form>
      {linkCard ? <FormLinkCard card={linkCard} floating /> : null}
    </section>
  );
}

function Asked({ field, problem }: { field: FormField; problem?: string }) {
  const id = useId();
  const describedBy = [field.help ? `${id}-help` : null, problem ? `${id}-error` : null].filter(Boolean).join(" ") || undefined;
  return (
    <div className={styles.asked} data-invalid={!!problem}>
      {field.type === "consent" ? (
        <label className={styles.consent}>
          <input type="checkbox" name={field.key} required={field.required} aria-describedby={describedBy} aria-invalid={!!problem || undefined} />
          <span>{field.label || "Unworded question"}{field.required ? <span className={styles.req}> *</span> : null}</span>
        </label>
      ) : (
        <p id={`${id}-label`} className={styles.askedLabel}>
          {field.label || <span className={styles.blank}>Unworded question</span>}
          {field.required ? <span className={styles.req}> *</span> : null}
        </p>
      )}
      {field.help ? <p id={`${id}-help`} className={styles.help}>{field.help}</p> : null}
      {field.type !== "consent" ? <Control field={field} labelId={`${id}-label`} describedBy={describedBy} invalid={!!problem} /> : null}
      {problem ? <ErrorDescription id={`${id}-error`}>{problem}</ErrorDescription> : null}
    </div>
  );
}

function Control({ field, labelId, describedBy, invalid }: { field: FormField; labelId: string; describedBy?: string; invalid: boolean }) {
  const shared = {
    name: field.key,
    className: styles.answerInput,
    required: field.required,
    "aria-labelledby": labelId,
    "aria-describedby": describedBy,
    "aria-invalid": invalid || undefined,
  };
  const lengthLimits = { minLength: field.minLength ?? undefined, maxLength: field.maxLength ?? undefined };

  switch (field.type) {
    case "paragraph":
      return <textarea {...shared} {...lengthLimits} rows={3} placeholder="Your answer" />;

    case "select":
      return (
        <div className={styles.previewSelect}>
          <Select {...shared} defaultValue="">
            <option value="">Select an option</option>
            {field.options.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </Select>
        </div>
      );

    case "radio":
    case "checkboxes":
      return (
        <fieldset className={styles.choices} data-multiple={field.type === "checkboxes"} aria-labelledby={labelId} aria-describedby={describedBy} aria-invalid={invalid || undefined}>
          {field.options.length === 0 ? (
            <p className="meta">Nothing to choose from.</p>
          ) : (
            field.options.map((option) => (
              /* The whole row is the target rather than the tick alone. That
                 is most of the difference between a choice question on a phone
                 that works and one that does not, and it is a difference an
                 author should be able to see here. */
              <label className={styles.choice} key={option.value}>
                <input
                  type={field.type === "radio" ? "radio" : "checkbox"}
                  name={field.key}
                  value={option.value}
                  required={field.type === "radio" && field.required}
                  aria-invalid={invalid || undefined}
                  aria-describedby={describedBy}
                />
                <span>{option.label}</span>
              </label>
            ))
          )}
        </fieldset>
      );

    case "file":
      return <input {...shared} type="file" accept="application/pdf,.pdf" />;

    case "email":
      return <input {...shared} {...lengthLimits} type="email" inputMode="email" autoCapitalize="none" spellCheck={false} placeholder="Your email" />;

    case "phone":
      return <input {...shared} {...lengthLimits} type="tel" inputMode="tel" placeholder="Your phone number" />;

    case "number":
      return <input {...shared} type="number" inputMode="decimal" min={field.min ?? undefined} max={field.max ?? undefined} placeholder="Enter a number" onWheel={event => event.currentTarget.blur()} />;

    case "date":
      return <input {...shared} type="date" />;

    default:
      return <input {...shared} {...lengthLimits} type="text" placeholder="Your answer" />;
  }
}
