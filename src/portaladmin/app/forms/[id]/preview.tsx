"use client";

import type { FormField } from "@/lib/api";
import styles from "./builder.module.css";

/**
 * What an applicant sees.
 *
 * Inside a disabled fieldset, so every control is inert. A preview somebody
 * can type into is one somebody will type into, and then wonder where their
 * answer went.
 *
 * Drawn as the applicant's page rather than as another panel in the console:
 * the same card, the same one stripe of accent at the top, questions set the
 * width they will actually be read at. The failure this catches is the one the
 * editor cannot show — a question that reads fine as a row in a form builder
 * and makes no sense as a question. Sixty words of agreement text look like a
 * label on the left and like a wall on the right, and that only holds if the
 * right-hand side is laid out the way the applicant will meet it.
 *
 * Worth the duplication with portalweb's eventual renderer for the same
 * reason.
 *
 * Page breaks are marked rather than obeyed. Everything is on screen at once
 * with a rule where each page starts, because the question this answers is
 * "where do the pages fall", and paging through the preview to find out is the
 * long way round to it.
 */
export function Preview({
  fields,
  formName,
}: {
  fields: FormField[];
  formName: string;
}) {
  // Which page each field lands on. Everything before the first page break is
  // page one, so a form with no breaks is the single page it has always been —
  // and the numbers are computed once here rather than by a counter mutated
  // inside the map below, which would be a render that depends on how many
  // times React chose to run it.
  const pages: number[] = [];
  let page = 1;
  for (const field of fields) {
    if (field.type === "section") {
      page += 1;
    }

    pages.push(page);
  }

  // The whole form is shown at once, with the breaks marked, rather than one
  // page at a time behind a Next button. This is the view an author uses to
  // check where the pages fall, and answering that question by clicking
  // through four pages is answering it badly. Page one is labelled only when
  // there is a second one — on a form with no breaks the label would be
  // furniture.
  const paged = page > 1;

  return (
    <section>
      <p className={styles.previewHead}>Preview</p>

      <div className={styles.preview}>
        {/* The applicant site's one stripe of colour, so this reads as the
            same surface rather than as a white box that happens to hold the
            same questions. */}
        <div className={styles.previewRule} />

        <div className={styles.previewTop}>
          {/* A paragraph rather than a heading. It is a picture of a heading on
              somebody else's page — putting it in this document's outline
              would give the console two titles for one screen. */}
          <p className={styles.previewTitle}>{formName}</p>
        </div>

        {/* The disabled fieldset is what makes every control below inert. It
            wraps only the questions, because a disabled fieldset around the
            title would grey out the one thing here that is not a control. */}
        <fieldset className={styles.previewBody} disabled>
          {fields.length === 0 ? (
            <p className="meta">Nothing to fill in yet.</p>
          ) : (
            <>
              {paged ? <p className={styles.pageMark}>Page 1</p> : null}
              {fields.map((field, at) =>
                field.type === "section" ? (
                  <Break key={field.key} field={field} page={pages[at]} />
                ) : (
                  <Asked key={field.key} field={field} />
                ),
              )}
            </>
          )}
        </fieldset>
      </div>
    </section>
  );
}

/** Where one page ends and the next one's heading begins. */
function Break({ field, page }: { field: FormField; page: number }) {
  return (
    <div className={styles.pageBreak}>
      <p className={styles.pageMark}>Page {page}</p>
      <p className={styles.pageHeading}>
        {field.label || <span className={styles.blank}>Unheaded page</span>}
      </p>
      {field.help ? <p className={styles.help}>{field.help}</p> : null}
    </div>
  );
}

function Asked({ field }: { field: FormField }) {
  // An agreement is a tick box with the question as its label, not a question
  // with a tick box under it. Rendering it the ordinary way puts a sixty-word
  // agreement in a heading and an unlabelled checkbox beneath.
  if (field.type === "consent") {
    return (
      <div className={styles.asked}>
        <label className={styles.consent}>
          <input type="checkbox" />
          <span>
            {field.label || <span className={styles.blank}>Unworded question</span>}
            {field.required ? <span className={styles.req}> *</span> : null}
          </span>
        </label>
        {field.help ? <p className={styles.help}>{field.help}</p> : null}
      </div>
    );
  }

  return (
    <div className={styles.asked}>
      <p className={styles.askedLabel}>
        {field.label || <span className={styles.blank}>Unworded question</span>}
        {field.required ? <span className={styles.req}> *</span> : null}
      </p>
      {field.help ? <p className={styles.help}>{field.help}</p> : null}
      <Control field={field} />
    </div>
  );
}

function Control({ field }: { field: FormField }) {
  switch (field.type) {
    case "paragraph":
      return <textarea rows={3} />;

    case "select":
      return (
        <select defaultValue="">
          <option value="">Choose…</option>
          {field.options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      );

    case "radio":
    case "checkboxes":
      return (
        <div className={styles.choices}>
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
                />
                {option.label}
              </label>
            ))
          )}
        </div>
      );

    case "file":
      return <input type="file" />;

    case "email":
      return <input type="email" />;

    case "phone":
      return <input type="tel" />;

    case "number":
      return <input type="number" />;

    case "date":
      return <input type="date" />;

    default:
      return <input type="text" />;
  }
}
