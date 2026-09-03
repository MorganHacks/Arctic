"use client";

import type { FormField } from "@/lib/api";

/**
 * What an applicant sees.
 *
 * Inside a disabled fieldset, so every control is inert. A preview somebody
 * can type into is one somebody will type into, and then wonder where their
 * answer went.
 *
 * Worth the duplication with portalweb's eventual renderer, because the
 * failure this catches is the one the builder cannot otherwise show: a
 * question that reads fine as a row in an editor and makes no sense as a
 * question. Sixty words of agreement text look like a label here and like a
 * wall there.
 *
 * Page breaks are marked rather than obeyed. Everything is on screen at once
 * with a rule where each page starts, because the question this answers is
 * "where do the pages fall", and paging through the preview to find out is the
 * long way round to it.
 */
export function Preview({ fields }: { fields: FormField[] }) {
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
    <fieldset className="preview" disabled>
      <legend className="meta">Preview</legend>

      {fields.length === 0 ? (
        <p className="meta">Nothing to fill in yet.</p>
      ) : (
        <>
          {paged ? <p className="page-mark">Page 1</p> : null}
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
  );
}

/** Where one page ends and the next one's heading begins. */
function Break({ field, page }: { field: FormField; page: number }) {
  return (
    <div className="page-mark-break">
      <p className="page-mark">Page {page}</p>
      <p className="page-heading">
        {field.label || <em className="meta">Unheaded page</em>}
      </p>
      {field.help ? <p className="meta">{field.help}</p> : null}
    </div>
  );
}

function Asked({ field }: { field: FormField }) {
  // An agreement is a tick box with the question as its label, not a question
  // with a tick box under it. Rendering it the ordinary way puts MLH's sixty
  // words in a heading and an unlabelled checkbox beneath.
  if (field.type === "consent") {
    return (
      <div className="asked">
        <label className="check consent">
          <input type="checkbox" />
          <span>
            {field.label || <em className="meta">Unworded question</em>}
            {field.required ? <span className="req"> *</span> : null}
          </span>
        </label>
        {field.help ? <p className="meta">{field.help}</p> : null}
      </div>
    );
  }

  return (
    <div className="asked">
      <p className="asked-label">
        {field.label || <em className="meta">Unworded question</em>}
        {field.required ? <span className="req"> *</span> : null}
      </p>
      {field.help ? <p className="meta">{field.help}</p> : null}
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
        <div className="choices">
          {field.options.length === 0 ? (
            <p className="meta">Nothing to choose from.</p>
          ) : (
            field.options.map((option) => (
              <label className="check" key={option.value}>
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
