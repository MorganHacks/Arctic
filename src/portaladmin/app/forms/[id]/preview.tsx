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
 */
export function Preview({ fields }: { fields: FormField[] }) {
  return (
    <fieldset className="preview" disabled>
      <legend className="meta">Preview</legend>

      {fields.length === 0 ? (
        <p className="meta">Nothing to fill in yet.</p>
      ) : (
        fields.map((field) => <Asked key={field.key} field={field} />)
      )}
    </fieldset>
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
